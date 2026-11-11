//! \file       ArcCPZ3.cs
//! \date       2026-06-17
//! \brief      Purple Software resource archive.
//
// Copyright (C) 2026 by morkt
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
    public class Cpz3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CPZ3"; } }
        public override string Description { get { return "CMVS engine resource archive"; } }
        public override uint     Signature { get { return 0x335A5043; } } // 'CPZ3'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Cpz3Opener ()
        {
            Extensions = new string[] { "cpz" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = (int)(file.View.ReadUInt32 (4) ^ 0x5E9C4F37);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (8) ^ 0xF32AED17u;
            uint key = file.View.ReadUInt32 (0x10) ^ 0xA62978E4u;
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
                entry.Key = LittleEndian.ToUInt32 (index, index_offset+0x14) ^ 0xC7F5DA63u;
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
            if (data.Length > 0x30 && Binary.AsciiEqual (data, 0, "PS2A"))
                data = CpzOpener.UnpackPs2 (data);
            else if (data.Length > 0x40 && Binary.AsciiEqual (data, 0, "PB3B"))
                CpzOpener.DecryptPb3 (data);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, uint key)
        {
            int shift = 0;
            int k = (int)key;
            for (int i = 0; i < 8; ++i)
            {
                shift ^= k & 0xF;
                k >>= 4;
            }
            shift ^= 0xD;
            shift += 8;
            unsafe
            {
                fixed (byte* data_fixed = data)
                {
                    uint* data32 = (uint*)data_fixed;
                    int table_ptr = 3;
                    for (int count = data.Length >> 2; count > 0; --count)
                    {
                        uint t = (*data32 ^ (EncryptionTable[table_ptr++ & 0xF] + key)) + 0x6E58A5C2u;
                        *data32++ = Binary.RotL (t, shift);
                    }
                    byte* data8 = (byte*)data32;
                    for (int count = data.Length & 3; count > 0; --count)
                    {
                        *data8 = (byte)((*data8 ^ ((EncryptionTable[table_ptr++ & 0xF] + key) >> (count * 4))) + 0x52);
                        ++data8;
                    }
                }
            }
        }

        static readonly uint[] EncryptionTable = {
            0x4D0D4A5E, 0xB3ABF3E1, 0x3C37336D, 0x86C3F5F3, 0x7D4F9B89, 0x58D7DE11, 0x6367778D, 0xA5F34629,
            0x067FA4B5, 0xED0AE742, 0xB19450CC, 0xE7204A5A, 0xD9AF04F5, 0x5D3B687F, 0xC1C7A6FD, 0xFC502289
        };
    }
}
