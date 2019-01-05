//! \file       ArcCPC.cs
//! \date       2019 Jan 02
//! \brief      Studio B-Room resource archive.
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

// [040604][Studio B-Room] Dice-ki! ~Koi wa Un Makase~

namespace GameRes.Formats.BRoom
{
    [Export(typeof(ArchiveFormat))]
    public class CpcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPC"; } }
        public override string Description { get { return "Studio B-Room resource archive"; } }
        public override uint     Signature { get { return 0x47435043; } } // 'CPCG'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = (int)(file.View.ReadUInt32 (4) ^ 0xFF559977);
            if (!IsSaneCount (count))
                return null;
            bool encryption_flag = (file.View.ReadByte (8) ^ 0x8A) != 0;
            int key_index = file.View.ReadByte (9) ^ 0xCE;
            if (encryption_flag && key_index > OffsetKey.Length)
                return null;
            uint index_offset = 12;
            var name_buffer = new byte[0x30];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                if (encryption_flag)
                {
                    uint key = IndexKey[i & 0x3F];
                    offset ^= key ^ OffsetKey[key_index];
                    size   ^= key ^ LengthKey[key_index];
                }
                else
                {
                    offset ^= (uint)i ^ 0x35846u;
                    size   ^= (uint)i ^ 0x57982525u;
                }
                file.View.Read (index_offset+8, name_buffer, 0, 0x30);
                int j;
                for (j = 0; j < 0x30; ++j)
                {
                    name_buffer[j] ^= NameKey[j];
                    if (0 == name_buffer[j])
                        break;
                }
                var name = Encodings.cp932.GetString (name_buffer, 0, j);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x38;
            }
            return new ArcFile (file, this, dir);
        }

        static readonly byte[] NameKey = {
            0x13, 0x60, 0xFC, 0x4D, 0xDE, 0xD0, 0x79, 0xB3, 0x51, 0xC5, 0xEC, 0x9E, 0x06, 0x82, 0x63, 0x73,
            0x21, 0xAB, 0xBF, 0x1A, 0x32, 0x9C, 0xBA, 0xFA, 0x5D, 0xFF, 0x29, 0x25, 0xB8, 0x7F, 0xCF, 0xF4,
            0x75, 0x93, 0x05, 0x40, 0x0C, 0xA3, 0x6A, 0x04, 0x98, 0x67, 0x47, 0xEF, 0x8B, 0xAD, 0x56, 0x65,
        };
        static readonly ushort[] IndexKey = {
            0x27F6, 0x940B, 0x611F, 0xD845, 0xE733, 0xE871, 0x8A11, 0x360E, 0xC7AA, 0x31BB, 0xB23A, 0xC957,
            0x28D2, 0xBF73, 0x1DFF, 0x29EB, 0xD3C2, 0x6CC6, 0xDF7B, 0xA22E, 0xB82B, 0x9256, 0xCEEC, 0xDC08,
            0xA96A, 0xE52D, 0x5F96, 0x7959, 0x81A4, 0x990D, 0x6826, 0xAF38, 0x1B01, 0x2A19, 0x679D, 0x494E,
            0x555C, 0xE623, 0xB797, 0x6214, 0x3CAD, 0xDECD, 0x775B, 0x16A7, 0x37CC, 0xE3AE, 0xD6D5, 0x9F9B,
            0x8C1E, 0xCAF3, 0x8BB1, 0x6DC5, 0x1320, 0xBA1A, 0x42BC, 0xED2F, 0xDAB9, 0xA89C, 0x53F9, 0x4691,
            0xF4E4, 0xFBD1, 0xE982, 0xBEB4,
        };
        static readonly uint[] OffsetKey = { 0x89D9A054, 0x74E297E9, 0xEECA074F, 0xF2A42CE8, 0x2D6FBE0E };
        static readonly uint[] LengthKey = { 0x101C2885, 0x5F7E52F8, 0x3812A6B4, 0x99696CA1, 0x6B0BA9A7 };
    }
}
