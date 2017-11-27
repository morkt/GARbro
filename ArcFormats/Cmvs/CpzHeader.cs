//! \file       CpzHeader.cs
//! \date       2017 Nov 27
//! \brief      CPZ archives header class.
//
// Copyright (C) 2017 by morkt
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
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Purple
{
    internal class CpzHeader
    {
        public int      Version;
        public int      DirCount;
        public int      DirEntriesSize;
        public int      FileEntriesSize;
        public uint[]   CmvsMd5 = new uint[4];
        public uint     MasterKey;
        public bool     IsEncrypted;
        public uint     EntryKey;
        public int      IndexKeySize;
        public uint     Checksum;

        public uint     InitChecksum = 0x923A564Cu;
        public uint     IndexOffset;
        public uint     IndexSize;
        public IEnumerable<byte> IndexMd5;
        public int      EntryNameOffset;

        public bool IsLongSize { get { return Version > 6; } }

        public static CpzHeader Parse (ArcView file)
        {
            var cpz = new CpzHeader { Version = file.View.ReadByte (3) - '0' };
            int checksum_length;
            byte[] header;
            if (cpz.Version < 7)
            {
                header = file.View.ReadBytes (0, 0x40);
                if (cpz.Version < 6)
                    cpz.ParseV5 (header);
                else
                    cpz.ParseV6 (header);
                cpz.IndexOffset = 0x40;
                cpz.IndexSize = (uint)(cpz.DirEntriesSize + cpz.FileEntriesSize);
                cpz.Checksum = header.ToUInt32 (0x3C);
                checksum_length = 0x3C;
            }
            else
            {
                header = file.View.ReadBytes (0, 0x48);
                cpz.ParseV7 (header);
                cpz.IndexOffset = 0x48;
                cpz.IndexSize = (uint)(cpz.DirEntriesSize + cpz.FileEntriesSize + cpz.IndexKeySize);
                cpz.Checksum = header.ToUInt32 (0x44);
                checksum_length = 0x40;
            }
            if (cpz.Checksum != CheckSum (header, 0, checksum_length, cpz.InitChecksum))
                return null;

            cpz.IndexMd5 = header.Skip (0x10).Take (0x10).ToArray();
            return cpz;
        }

        private void ParseV5 (byte[] header)
        {
            DirCount        = -0x1C5AC27 ^ LittleEndian.ToInt32 (header, 4);
            DirEntriesSize  = 0x37F298E7 ^ LittleEndian.ToInt32 (header, 8);
            FileEntriesSize = 0x7A6F3A2C ^ LittleEndian.ToInt32 (header, 0x0C);
            MasterKey       = 0xAE7D39BF ^ LittleEndian.ToUInt32 (header, 0x30);
            IsEncrypted     = 0 != (0xFB73A955 ^ LittleEndian.ToUInt32 (header, 0x34));
            EntryKey        = 0;
            EntryNameOffset = 0x18;
            CmvsMd5[0] = 0x43DE7C19 ^ LittleEndian.ToUInt32 (header, 0x20);
            CmvsMd5[1] = 0xCC65F415 ^ LittleEndian.ToUInt32 (header, 0x24);
            CmvsMd5[2] = 0xD016A93C ^ LittleEndian.ToUInt32 (header, 0x28);
            CmvsMd5[3] = 0x97A3BA9A ^ LittleEndian.ToUInt32 (header, 0x2C);
        }

        private void ParseV6 (byte[] header)
        {
            uint entry_key  = 0x37ACF832 ^ LittleEndian.ToUInt32 (header, 0x38);
            DirCount        = -0x1C5AC26 ^ LittleEndian.ToInt32 (header, 4);
            DirEntriesSize  = 0x37F298E8 ^ LittleEndian.ToInt32 (header, 8);
            FileEntriesSize = 0x7A6F3A2D ^ LittleEndian.ToInt32 (header, 0x0C);
            MasterKey       = 0xAE7D39B7 ^ LittleEndian.ToUInt32 (header, 0x30);
            IsEncrypted     = 0 != (0xFB73A956 ^ LittleEndian.ToUInt32 (header, 0x34));
            EntryKey        = 0x7DA8F173 * Binary.RotR (entry_key, 5) + 0x13712765;
            EntryNameOffset = 0x18;
            CmvsMd5[0] = 0x43DE7C1A ^ LittleEndian.ToUInt32 (header, 0x20);
            CmvsMd5[1] = 0xCC65F416 ^ LittleEndian.ToUInt32 (header, 0x24);
            CmvsMd5[2] = 0xD016A93D ^ LittleEndian.ToUInt32 (header, 0x28);
            CmvsMd5[3] = 0x97A3BA9B ^ LittleEndian.ToUInt32 (header, 0x2C);
        }

        private void ParseV7 (byte[] header)
        {
            ParseV6 (header);
            var index_key_size = LittleEndian.ToInt32 (header, 0x40);
            IndexKeySize = 0x65EF99F3 ^ index_key_size;
            InitChecksum = (uint)index_key_size - 0x6DC5A9B4u;
            EntryNameOffset = 0x1C;
        }

        public bool VerifyIndex (byte[] index)
        {
            if (index.Length != (int)IndexSize)
                return false;
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash (index);
                if (!hash.SequenceEqual (IndexMd5))
                    return false;
                if (Version > 6 && IndexKeySize > 0x10)
                {
                    int index_size = DirEntriesSize + FileEntriesSize;
                    hash = md5.ComputeHash (index, index_size+0x10, IndexKeySize-0x10);
                    if (!hash.SequenceEqual (index.Skip (index_size).Take (0x10)))
                        return false;
                }
                return true;
            }
        }

        public static uint CheckSum (byte[] data, int index, int length, uint crc)
        {
            if (null == data)
                throw new ArgumentNullException ("data");
            if (index < 0 || index > data.Length)
                throw new ArgumentOutOfRangeException ("index");
            if (length < 0 || length > data.Length || length > data.Length-index)
                throw new ArgumentException ("length");
            int dwords = length / 4;
            if (dwords > 0)
            {
                unsafe
                {
                    fixed (byte* raw = &data[index])
                    {
                        uint* raw32 = (uint*)raw;
                        for (int i = 0; i < dwords; ++i)
                            crc += raw32[i];
                    }
                }
                index += length & -4;
            }
            for (int i = 0; i < (length & 3); ++i)
                crc += data[index+i];
            return crc;
        }
    }
}
