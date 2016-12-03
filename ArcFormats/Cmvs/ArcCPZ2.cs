//! \file       ArcCPZ2.cs
//! \date       Fri Dec 02 22:24:17 2016
//! \brief      Purple Software resource archive.
//
// Copyright (C) 2016 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Purple
{
    [Export(typeof(ArchiveFormat))]
    public class Cpz2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CPZ2"; } }
        public override string Description { get { return "CVNS engine resource archive"; } }
        public override uint     Signature { get { return 0x325A5043; } } // 'CPZ2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Cpz2Opener ()
        {
            Extensions = new string[] { "cpz" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = (int)(file.View.ReadUInt32 (4) ^ 0xE47C59F3);
            if (!IsSaneCount (count))
                return null;
	        uint index_size = file.View.ReadUInt32 (8) ^ 0x3F71DE2Au;
	        uint key = file.View.ReadUInt32 (0x10) ^ 0x40DE832Cu;
            var index = file.View.ReadBytes (0x14, index_size);
            DecryptData (index, key);
            long base_offset = 0x14 + index_size;
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int entry_size = LittleEndian.ToInt32 (index, index_offset);
                if (entry_size <= 0 || entry_size > index.Length - index_offset)
                    return null;
                var name = Binary.GetCString (index, index_offset+0x18);
                var entry = FormatCatalog.Instance.Create<CpzEntry> (name);
                entry.Size = LittleEndian.ToUInt32 (index, index_offset+4);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+8) + base_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Key = LittleEndian.ToUInt32 (index, index_offset+0x14) ^ 0x796C3AFDu;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var cent = entry as CpzEntry;
            if (null == cent)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, cent.Key);
            if (Binary.AsciiEqual (data, "PSS0"))
                data = CpzOpener.UnpackLzss (data);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, uint key)
        {
            int shift = 5;
            int k = (int)key;
            for (int i = 0; i < 8; ++i)
            {
                shift ^= k & 0xF;
                k >>= 4;
            }
            shift += 8;
            unsafe
            {
                fixed (byte* data_fixed = data)
                {
                    uint* data32 = (uint*)data_fixed;
                    int table_ptr = 0;
                    for (int count = data.Length >> 2; count > 0; --count)
                    {
                        uint t = (*data32 ^ (EncryptionTable[table_ptr++ & 0xF] + key)) - 0x15C3E7u;
                        *data32++ = Binary.RotR (t, shift);
                    }
                    byte* data8 = (byte*)data32;
                    shift = 0;
                    for (int count = data.Length & 3; count > 0; --count)
                    {
                        *data8 = (byte)((*data8 ^ ((EncryptionTable[table_ptr++ & 0xF] + key) >> shift)) + 0x37);
                        shift += 4;
                        ++data8;
                    }
                }
            }
        }

        static readonly uint[] EncryptionTable = {
            0x3A68CDBF, 0xD3C3A711, 0x8414876E, 0x657BEFDB, 0xCDD7C125, 0x09328580, 0x288FFEDD, 0x99EBF13A,
            0x5A471F95, 0x1EA3F4F1, 0xF4FF524E, 0xD358E8A9, 0xC5B71015, 0xA913046F, 0x2D6FD2BD, 0x68C8BE19
        };
    }
}
