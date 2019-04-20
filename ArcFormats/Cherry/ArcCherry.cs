//! \file       ArcCherry.cs
//! \date       Wed Jun 24 21:22:56 2015
//! \brief      Cherry Soft archives implementation.
//
// Copyright (C) 2015 by morkt
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Cherry
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/CHERRY"; } }
        public override string Description { get { return "Cherry Soft PACK resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            long base_offset = file.View.ReadUInt32 (4);
            if (base_offset >= file.MaxOffset || base_offset != (8 + count*0x18))
                return null;
            var dir = ReadIndex (file, 8, count, base_offset, file);
            return dir != null ? new ArcFile (file, this, dir) : null;
        }

        protected List<Entry> ReadIndex (ArcView index, int index_offset, int count, long base_offset, ArcView file)
        {
            uint index_size = (uint)count * 0x18u;
            if (index_size > index.View.Reserve (index_offset, index_size))
                return null;
            string arc_name = Path.GetFileNameWithoutExtension (file.Name);
            bool is_grp = arc_name.EndsWith ("GRP", StringComparison.InvariantCultureIgnoreCase);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = index.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                var offset = base_offset + index.View.ReadUInt32 (index_offset+0x10);
                Entry entry;
                if (is_grp)
                {
                    entry = new Entry {
                        Name = Path.ChangeExtension (name, "grp"),
                        Type = "image",
                        Offset = offset
                    };
                }
                else
                {
                    entry = AutoEntry.Create (file, offset, name);
                }
                entry.Size = index.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "GsWIN SC File"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var text_offset = 0x68 + arc.File.View.ReadUInt32 (entry.Offset+0x5C);
            var text_size = arc.File.View.ReadUInt32 (entry.Offset+0x60);
            if (0 == text_size || text_offset+text_size > entry.Size)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (uint i = 0; i < text_size; ++i)
            {
                data[text_offset+i] ^= (byte)i;
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }

    internal class CherryPak : ArcFile
    {
        public CherryPak (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak2Opener : PakOpener
    {
        public override string         Tag { get { return "PAK/CHERRY2"; } }
        public override string Description { get { return "Cherry Soft PACK resource archive v2"; } }
        public override uint     Signature { get { return 0x52454843; } } // 'CHER'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Pak2Opener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CHERRY PACK 2.0\0") &&
                !file.View.AsciiEqual (0, "CHERRY PACK 3.0\0"))
                return null;
            int version = file.View.ReadByte (0xC) - '0';
            bool is_compressed = file.View.ReadInt32 (0x10) != 0;
            int count = file.View.ReadInt32 (0x14);
            long base_offset = file.View.ReadUInt32 (0x18);
            bool is_encrypted = false;
            while (!IsSaneCount (count) || base_offset >= file.MaxOffset
                   || (2 == version && !is_compressed && base_offset != (0x1C + count * 0x18)))
            {
                if (is_encrypted)
                    return null;
                // these keys seem to be constant across different games
                count       ^= unchecked((int)0xBC138744);
                base_offset ^= 0x64E0BA23; 
                is_encrypted = true;
            }
            List<Entry> dir;
            if (is_compressed)
            {
                var packed = file.View.ReadBytes (0x1C, (uint)base_offset-0x1C);
                Decrypt (packed, 0, packed.Length);
                using (var mem = new MemoryStream (packed))
                using (var lzss = new LzssStream (mem))
                using (var index = new ArcView (lzss, file.Name, (uint)count * 0x18))
                    dir = ReadIndex (index, 0, count, base_offset, file);
            }
            else
            {
                dir = ReadIndex (file, 0x1C, count, base_offset, file);
            }
            if (null == dir)
                return null;
            if (is_encrypted && is_compressed)
                return new CherryPak (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        internal static void Decrypt (byte[] data, int index, int length) // Exile ~Blood Royal 2~
        {
            for (int i = 0; i+1 < length; i += 2)
            {
                byte lo = (byte)(data[index+i  ] ^ 0x33);
                byte hi = (byte)(data[index+i+1] ^ 0xCC);
                data[index+i  ] = hi;
                data[index+i+1] = lo;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!(arc is CherryPak) || entry.Size < 0x18)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (data.Length >= 0x18)
            {
                unsafe
                {
                    fixed (byte* raw = data)
                    {
                        uint* raw32 = (uint*)raw;
                        raw32[0] ^= 0xA53CC35Au; // FIXME: Exile-specific?
                        raw32[1] ^= 0x35421005u;
                        raw32[4] ^= 0xCF42355Du;
                    }
                }
                Decrypt (data, 0x18, (int)(data.Length - 0x18));
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
