//! \file       ArcFLT.cs
//! \date       2018 Oct 12
//! \brief      FrontWing resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.FrontWing
{
    internal class FltEntry : PackedEntry
    {
        public int  Compression;
        public bool IsEncrypted;

        public byte Key {
            get {
                long key = this.Offset;
                return (byte)(key ^ (key >> 8) ^ (key >> 16) ^ (key >> 24)
                              ^ (key >> 32) ^ (key >> 40) ^ (key >> 48) ^ (key >> 56));
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class FltOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FLT"; } }
        public override string Description { get { return "FrontWing resource archive"; } }
        public override uint     Signature { get { return 0x5F42494C; } } // 'LIB_PACKDATA0000'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACKDATA0000"))
                return null;
            int count = file.View.ReadInt32 (0x14);
            if (!IsSaneCount (count))
                return null;
            bool is_encrypted = file.View.ReadInt32 (0x1C) == 1;
            var index = file.View.ReadBytes (0x100, (uint)count*0x100);
            if (is_encrypted)
                DecryptIndex (index, 0, index.Length);
            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_len = 0;
                while (name_len < 0xE8)
                {
                    if (index[index_pos+name_len] == 0 && index[index_pos+name_len+1] == 0)
                        break;
                    name_len += 2;
                }
                if (0 == name_len)
                    return null;
                var name = Encoding.Unicode.GetString (index, index_pos, name_len);
                var entry = Create<FltEntry> (name);
                entry.Compression = index[index_pos+0xEA];
                entry.IsEncrypted = index[index_pos+0xEB] == 1;
                entry.Size = index.ToUInt32 (index_pos+0xF0);
                entry.UnpackedSize = index.ToUInt32 (index_pos+0xF4);
                entry.Offset = index.ToInt64 (index_pos+0xF8);
                entry.IsPacked = entry.Compression == 1;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x100;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var fent = (FltEntry)entry;
            Stream input = arc.File.CreateStream (fent.Offset, fent.Size);
            if (fent.IsEncrypted)
                input = new XoredStream (input, fent.Key);
            if (fent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }

        void DecryptIndex (byte[] data, int pos, int length)
        {
            for (int i = 0; i < length; ++i)
                data[pos+i] = DefaultNameKey[data[pos+i]];
        }

        static readonly byte[] DefaultNameKey = {
            0x00, 0x16, 0xC9, 0x4A, 0x91, 0x04, 0x5E, 0x20, 0x33, 0x14, 0x4B, 0x8A, 0x0A, 0x70, 0x9F, 0x36,
            0xAF, 0x0D, 0x93, 0xB0, 0x2B, 0xFE, 0x29, 0x72, 0x94, 0x99, 0x9B, 0xED, 0xCE, 0xC4, 0xF1, 0xF4,
            0x9C, 0x1B, 0xE0, 0x02, 0x87, 0x82, 0x47, 0xDF, 0xF3, 0xA9, 0xDC, 0xEF, 0x3B, 0xB9, 0xC5, 0x83,
            0xD8, 0x0F, 0x9A, 0xE2, 0xBD, 0x28, 0x9E, 0xAB, 0xB7, 0x3F, 0x75, 0x63, 0x2A, 0x5D, 0x05, 0x4E,
            0x1F, 0xCF, 0x61, 0xAA, 0x10, 0x77, 0xCC, 0x90, 0xD5, 0x43, 0xA6, 0xEC, 0x88, 0x08, 0x97, 0x7A,
            0xF5, 0x42, 0xBA, 0x3A, 0xF6, 0x7D, 0x8F, 0xB5, 0x18, 0x76, 0x40, 0x6D, 0xAC, 0x19, 0x1D, 0x4D,
            0x38, 0x03, 0x8C, 0x01, 0x7B, 0xE7, 0xB3, 0x2F, 0x67, 0xF8, 0x6A, 0x13, 0xAD, 0xF0, 0x5B, 0x7C,
            0x24, 0x6B, 0xC6, 0xC0, 0x06, 0x89, 0x71, 0xDD, 0x23, 0x11, 0x09, 0x1A, 0xF9, 0xC2, 0x31, 0xB1,
            0xBE, 0x8B, 0x6E, 0xFA, 0x48, 0x52, 0xDA, 0x17, 0x21, 0xD9, 0x60, 0x78, 0xA4, 0xA8, 0x26, 0x79,
            0x5C, 0x41, 0xBF, 0xD4, 0x3C, 0x1E, 0x86, 0xA7, 0x6F, 0xB8, 0x2C, 0xD2, 0x57, 0x56, 0x58, 0x66,
            0xE4, 0xCA, 0x55, 0x44, 0xE8, 0x85, 0x53, 0x96, 0x7F, 0x68, 0xC7, 0x73, 0x4C, 0xE6, 0x12, 0xB6,
            0x98, 0xBC, 0xAE, 0xEE, 0xA0, 0xFC, 0x69, 0x62, 0xC1, 0xE3, 0xB2, 0x95, 0xE9, 0x46, 0xCD, 0xD0,
            0x50, 0x15, 0x9D, 0x51, 0x30, 0x5A, 0x64, 0xF7, 0x8E, 0x07, 0xBB, 0xC8, 0xA2, 0x3E, 0xD3, 0x39,
            0xA5, 0x49, 0x5F, 0x3D, 0xD1, 0xCB, 0x0E, 0x54, 0xC3, 0x4F, 0x8D, 0x84, 0xDB, 0x2D, 0x0B, 0xD7,
            0x92, 0x7E, 0xE1, 0xEB, 0x81, 0xFD, 0x25, 0xEA, 0x2E, 0xB4, 0xD6, 0x37, 0xA1, 0xE5, 0x6C, 0x1C,
            0x22, 0x45, 0xF2, 0x65, 0x74, 0x34, 0x35, 0xDE, 0x59, 0x27, 0xA3, 0xFB, 0x0C, 0x80, 0x32, 0xFF,
        };
    }
}
