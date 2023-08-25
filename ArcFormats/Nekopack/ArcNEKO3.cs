//! \file       ArcNEKO3.cs
//! \date       2022 Jun 17
//! \brief      Nekopack archive format implementation.
//
// Copyright (C) 2022 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Neko
{
    internal class Neko3Archive : ArcFile
    {
        public readonly byte[] Key;

        public Neko3Archive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    internal class Neko3Entry : Entry
    {
        public ushort   Seed;
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "NEKOPACK/3"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4F4B454E; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Pak3Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK") || file.MaxOffset <= 0x410)
                return null;

            uint seed = file.View.ReadUInt32 (0xC);
            int count = (int)(seed % 7u) + 3;
            var key = file.View.ReadBytes (0x10, 0x400);
            while (count --> 0)
            {
                Decrypt (key, 0x400, key, (ushort)seed, true);
            }
            seed = file.View.ReadUInt16 (0x410);
            var index_info = file.View.ReadBytes (0x414, 8);
            Decrypt (index_info, 8, key, (ushort)seed);
            uint index_size = LittleEndian.ToUInt32 (index_info, 0);
            long data_offset = 0x41CL + index_size;
            if (data_offset >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0x41C, index_size);
            Decrypt (index, index.Length, key, (ushort)seed);

            var dir = new List<Entry>();
            using (var input = new BinMemoryStream (index, file.Name))
            {
                int dir_count = input.ReadInt32();
                if (!IsSaneCount (dir_count))
                    return null;
                for (int d = 0; d < dir_count; ++d)
                {
                    int name_len = input.ReadUInt8();
                    string dir_name = input.ReadCString (name_len);
                    if (string.IsNullOrEmpty (dir_name))
                        return null;
                    int file_count = input.ReadInt32();
                    if (!IsSaneCount (file_count))
                        return null;
                    for (int i = 0; i < file_count; ++i)
                    {
                        input.ReadByte();
                        name_len = input.ReadUInt8();
                        string name = input.ReadCString (name_len);
                        name = string.Join ("/", dir_name, name);
                        var entry = Create<Neko3Entry> (name);
                        entry.Offset = data_offset + input.ReadUInt32();
                        dir.Add (entry);
                    }
                }
            }
            var buffer = new byte[12];
            foreach (Neko3Entry entry in dir)
            {
                entry.Seed = file.View.ReadUInt16 (entry.Offset);
                file.View.Read (entry.Offset+4, buffer, 0, 8);
                Decrypt (buffer, 8, key, entry.Seed);
                entry.Size = LittleEndian.ToUInt32 (buffer, 0);
                entry.Offset += 12;
            }
            return new Neko3Archive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var narc = (Neko3Archive)arc;
            var nent = (Neko3Entry)entry;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            Decrypt (data, data.Length, narc.Key, nent.Seed);
            return new BinMemoryStream (data, entry.Name);
        }

        void Decrypt (byte[] data, int length, byte[] key, ushort seed, bool init = false)
        {
            int count = length / 4;
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    uint* data32 = (uint*)data8;
                    while (count --> 0)
                    {
                        uint s = *data32;
                        seed = (ushort)((seed + 0xC3) & 0x1FF);
                        uint d = s ^ LittleEndian.ToUInt32 (key, seed);
                        if (init)
                            seed += (ushort)s;
                        else
                            seed += (ushort)d;
                        *data32++ = d;
                    }
                }
            }
        }
    }
}
