//! \file       ArcDAT.cs
//! \date       2019 Feb 26
//! \brief      Unity PMaster engine resource archive.
//
// Copyright (C) 2019 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Unity.PMaster
{
    internal class PMasterEntry : Entry
    {
        public uint Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PMASTER"; } }
        public override string Description { get { return "Unity PMaster engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = 0;
            for (int i = 0; i < 0x400; i += 4)
                count += file.View.ReadInt32 (i);
            if (!IsSaneCount (count))
                return null;
            uint index_length = (uint)count * 0x10;
            if (index_length >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0x400, index_length);
            DecryptData (index, file.View.ReadUInt32 (0xD4));

            uint first_offset = index.ToUInt32 (4);
            if (first_offset >= file.MaxOffset || first_offset <= (0x400 + index_length))
                return null;
            uint names_length = first_offset - (0x400 + index_length);
            var names = file.View.ReadBytes (0x400 + index_length, names_length);
            DecryptData (names, file.View.ReadUInt32 (0x5C));

            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_pos = index.ToInt32 (index_pos);
                var name = Binary.GetCString (names, name_pos);
                var entry = Create<PMasterEntry> (name);
                entry.Offset = index.ToUInt32 (index_pos+4);
                entry.Size   = index.ToUInt32 (index_pos+8);
                entry.Key    = index.ToUInt32 (index_pos+12);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 16;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PMasterEntry)entry;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, pent.Key);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, uint seed)
        {
            var key = GenerateKey (seed);
            for (int i = 0; i < data.Length; i++)
            {
                byte b = data[i];
                b ^= key[i & 0xFF];
                b += 0x4D;
                b += key[i % 0x2B];
                b -= key[i & 0xFF];
                b ^= 0x23;
                data[i] = b;
            }
        }

        byte[] GenerateKey (uint seed)
        {
            var key = new byte[256];
            uint n = seed * 2281 + 59455;
            uint n2 = (n << 17) ^ n;
            for (int i = 0; i < 256; i++)
            {
                n >>= 5;
                n ^= n2;
                n *= 471;
                n -= seed;
                n += n2;
                n2 = n + 87;
                n ^= n2 & 91;
                key[i] = (byte)n;
                n >>= 1;
            }
            return key;
        }
    }
}
