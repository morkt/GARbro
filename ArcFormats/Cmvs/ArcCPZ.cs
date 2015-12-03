//! \file       ArcCPZ.cs
//! \date       Tue Nov 24 11:27:23 2015
//! \brief      Purple Software resource archive.
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
using System.Linq;
using GameRes.Utility;
using System.Security.Cryptography;

namespace GameRes.Formats.Purple
{
    [Serializable]
    public class CpzScheme : ResourceScheme
    {
        public uint[]   Cpz5Secret;
    }

    internal class CpzEntry : Entry
    {
        public uint CheckSum;
        public uint Key;
        public uint DirKey;
    }

    internal class CpzHeader
    {
        public int      DirCount;
        public int      DirEntriesSize;
        public int      FileEntriesSize;
        public uint[]   CmvsMd5 = new uint[4];
        public uint     MasterKey;
        public bool     IsEncrypted;
        public bool     IsCompressed;
    }

    internal class CpzArchive : ArcFile
    {
        public CpzHeader    Header;
        public Cpz5Decoder  Decoder;

        public CpzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, CpzHeader header, Cpz5Decoder decoder)
            : base (arc, impl, dir)
        {
            Header = header;
            Decoder = decoder;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class CpzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPZ"; } }
        public override string Description { get { return "Purple Software resource archive"; } }
        public override uint     Signature { get { return 0x355A5043; } } // 'CPZ5'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public static CpzScheme CmvsScheme = new CpzScheme();

        public override ResourceScheme Scheme
        {
            get { return CmvsScheme; }
            set { CmvsScheme = (CpzScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (null == CmvsScheme.Cpz5Secret)
                throw new OperationCanceledException ("Outdated encryption schemes database");
            var header = file.View.ReadBytes (0, 0x3C);
            var checksum = file.View.ReadUInt32 (0x3C);
            if (checksum != CheckSum (header, 0, 0x3C, 0x923A564Cu))
                return null;
            var cpz = new CpzHeader
            {
                DirCount        = -0x1C5AC27 ^ LittleEndian.ToInt32 (header, 4),
                DirEntriesSize  = 0x37F298E7 ^ LittleEndian.ToInt32 (header, 8),
                FileEntriesSize = 0x7A6F3A2C ^ LittleEndian.ToInt32 (header, 0x0C),
                MasterKey       = 0xAE7D39BF ^ LittleEndian.ToUInt32 (header, 0x30),
                IsEncrypted     = 0 != (0xFB73A955 ^ LittleEndian.ToUInt32 (header, 0x34)),
                IsCompressed    = 0 != (0x37ACF831 ^ LittleEndian.ToUInt32 (header, 0x38)),
            };
            cpz.CmvsMd5[0] = 0x43DE7C19 ^ LittleEndian.ToUInt32 (header, 0x20);
            cpz.CmvsMd5[1] = 0xCC65F415 ^ LittleEndian.ToUInt32 (header, 0x24);
            cpz.CmvsMd5[2] = 0xD016A93C ^ LittleEndian.ToUInt32 (header, 0x28);
            cpz.CmvsMd5[3] = 0x97A3BA9A ^ LittleEndian.ToUInt32 (header, 0x2C);

            var cmvs_md5 = new Cmvs.Md5VariantB();
            cmvs_md5.Compute (cpz.CmvsMd5);

            int index_size = cpz.DirEntriesSize + cpz.FileEntriesSize;
            var index = file.View.ReadBytes (0x40, (uint)index_size);
            if (index.Length != index_size)
                return null;

            var md5 = MD5.Create();
            var hash = md5.ComputeHash (index);
            if (!header.Skip (0x10).Take (0x10).SequenceEqual (hash))
                return null;

            DecryptIndexStage1 (index, cpz.MasterKey ^ 0x3795B39A);

            // variantA: 0x1A743125, variantB: 0x1A740235
            var decoder = new Cpz5Decoder (cpz.MasterKey, cpz.CmvsMd5[1], 0x1A740235);
            decoder.Decode (index, 0, cpz.DirEntriesSize, 0x3A);

            var key = new uint[4];
            key[0] = cpz.CmvsMd5[0] ^ (cpz.MasterKey + 0x76A3BF29);
            key[1] = cpz.CmvsMd5[1] ^  cpz.MasterKey;
            key[2] = cpz.CmvsMd5[2] ^ (cpz.MasterKey + 0x10000000);
            key[3] = cpz.CmvsMd5[3] ^  cpz.MasterKey;

            DecryptIndexDirectory (index, cpz.DirEntriesSize, key);

            decoder.Init (cpz.MasterKey, cpz.CmvsMd5[2], 0x1A740235);
            int base_offset = 0x40 + index_size;
            int dir_offset = 0;
            var dir = new List<Entry>();
            for (int i = 0; i < cpz.DirCount; ++i)
            {
                int dir_size = LittleEndian.ToInt32 (index, dir_offset);
                if (dir_size <= 0x10 || dir_size > index.Length)
                    return null;
                int  file_count     = LittleEndian.ToInt32 (index, dir_offset+4);
                int  entries_offset = LittleEndian.ToInt32 (index, dir_offset+8);
                uint dir_key        = LittleEndian.ToUInt32 (index, dir_offset+0xC);
                var  dir_name       = Binary.GetCString (index, dir_offset+0x10, dir_size-0x10);

                int next_entries_offset;
                if (i + 1 == cpz.DirCount)
                    next_entries_offset = cpz.FileEntriesSize;
                else
                    next_entries_offset = LittleEndian.ToInt32 (index, dir_offset + dir_size + 8);

                int cur_entries_size = next_entries_offset - entries_offset;

                int cur_offset = cpz.DirEntriesSize + entries_offset;
                int cur_entries_end = cur_offset + cur_entries_size;
                decoder.Decode (index, cur_offset, cur_entries_size, 0x7E);

                key[0] = cpz.CmvsMd5[0] ^ dir_key;
                key[1] = cpz.CmvsMd5[1] ^ (dir_key + 0x112233);
                key[2] = cpz.CmvsMd5[2] ^ dir_key;
                key[3] = cpz.CmvsMd5[3] ^ (dir_key + 0x34258765);

                DecryptIndexEntry (index, cur_offset, cur_entries_size, key);
                bool is_root_dir = dir_name == "root";

                dir.Capacity = dir.Count + file_count;
                for (int j = 0; j < file_count; ++j)
                {
                    int entry_size = LittleEndian.ToInt32 (index, cur_offset);
                    if (entry_size > index.Length || entry_size <= 0x18)
                        throw new UnknownEncryptionScheme();
                    var name = Binary.GetCString (index, cur_offset+0x18, cur_entries_end-(cur_offset+0x18));
                    if (!is_root_dir)
                        name = Path.Combine (dir_name, name);
                    var entry = FormatCatalog.Instance.Create<CpzEntry> (name);
                    entry.Offset    = LittleEndian.ToInt64 (index, cur_offset+4) + base_offset;
                    entry.Size      = LittleEndian.ToUInt32 (index, cur_offset+0xC);
                    entry.CheckSum  = LittleEndian.ToUInt32 (index, cur_offset+0x10);
                    entry.Key       = LittleEndian.ToUInt32 (index, cur_offset+0x14);
                    entry.DirKey    = dir_key;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    cur_offset += entry_size;
                }
                dir_offset += dir_size;
            }
            if (cpz.IsEncrypted)
                decoder.Init (cpz.CmvsMd5[3], cpz.MasterKey, 0x1A740235);
            return new CpzArchive (file, this, dir, cpz, decoder);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var carc = arc as CpzArchive;
            var cent = entry as CpzEntry;
            if (null == carc || null == cent)
                return base.OpenEntry (arc, entry);

            var data = carc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (carc.Header.IsEncrypted)
            {
                uint key = (carc.Header.MasterKey ^ (cent.DirKey + cent.Key)) + (uint)carc.Header.DirCount - 0x5C29E87B;
                carc.Decoder.DecryptEntry (data, carc.Header.CmvsMd5, key);
            }
            if (data.Length > 0x30 && Binary.AsciiEqual (data, 0, "PS2A"))
                data = UnpackPs2 (data);
            else if (data.Length > 0x40 && Binary.AsciiEqual (data, 0, "PB3B"))
                DecryptPb3 (data);
            return new MemoryStream (data);
        }

        void DecryptIndexStage1 (byte[] data, uint key)
        {
            var sectet_key = new uint[24];
            int secret_length = Math.Min (24, CmvsScheme.Cpz5Secret.Length);
            for (int i = 0; i < secret_length; ++i)
                sectet_key[i] = CmvsScheme.Cpz5Secret[i] - key;

            uint shift = key;
            shift = (shift >> 8) ^ key;
            shift = (shift >> 8) ^ key;
            shift = (shift >> 8) ^ key;
            shift = (shift >> 8) ^ key;
            shift = ((shift ^ 0xB) & 0xF) + 7;

            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    int i = 5;
                    for (int n = data.Length / 4; n > 0; --n)
                    {
                        *data32 = Binary.RotR ((sectet_key[i] ^ *data32) + 0x784C5962, (int)shift) + 0x01010101;
                        ++data32;
                        i = (i + 1) % 24;
                    }
                    byte* data8 = (byte*)data32;
                    for (int n = data.Length & 3; n > 0; --n)
                    {
                        *data8 = (byte)((*data8 ^ (sectet_key[i] >> (n * 4))) - 0x79);
                        ++data8;
                        i = (i + 1) % 24;
                    }
                }
            }
        }

        void DecryptIndexDirectory (byte[] data, int length, uint[] key)
        {
            uint seed = 0x76548AEF;
            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    int i;
                    for (i = 0; i < length / 4; ++i)
                    {
                        *data32 = Binary.RotL ((*data32 ^ key[i & 3]) - 0x4A91C262, 3) - seed;
                        ++data32;
                        seed += 0x10FB562A;
                    }
                    byte* data8 = (byte*)data32;
                    for (int j = length & 3; j > 0; --j)
                    {
                        *data8 = (byte)((*data8 ^ (key[i++ & 3] >> 6)) + 0x37);
                        ++data8;
                    }
                }
            }
        }

        void DecryptIndexEntry (byte[] data, int offset, int length, uint[] key)
        {
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException ("offset");
            if (length < 0 || length > data.Length || length > data.Length-offset)
                throw new ArgumentException ("length");
            uint seed = 0x2A65CB4E;
            unsafe
            {
                fixed (byte* raw = &data[offset])
                {
                    uint* data32 = (uint*)raw;
                    int i;
                    for (i = 0; i < length / 4; ++i)
                    {
                        *data32 = Binary.RotL ((*data32 ^ key[i & 3]) - seed, 2) + 0x37A19E8B;
                        ++data32;
                        seed -= 0x139FA9B;
                    }
                    byte* data8 = (byte*)data32;
                    for (int j = length & 3; j > 0; --j)
                    {
                        *data8 = (byte)((*data8 ^ (key[i++ & 3] >> 4)) + 5);
                        ++data8;
                    }
                }
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

        byte[] UnpackPs2 (byte[] data)
        {
            DecryptPs2 (data);
            byte[] frame = new byte[0x800];
            int frame_pos = 0x7DF;
            int unpacked_size = LittleEndian.ToInt32 (data, 0x28);
            byte[] output = new byte[0x30+unpacked_size];
            Buffer.BlockCopy (data, 0, output, 0, 0x30);
            int src = 0x30;
            int dst = 0x30;
            int ctl = 1;
            while (dst < output.Length && src < data.Length)
            {
                if (1 == ctl)
                    ctl = data[src++] | 0x100;
                if (0 != (ctl & 1))
                {
                    byte b = data[src++];
                    output[dst++] = b;
                    frame[frame_pos++] = b;
                    frame_pos &= 0x7FF;
                }
                else
                {
                    int lo = data[src++];
                    int hi = data[src++];
                    int offset = lo | (hi & 0xE0) << 3;
                    int count = (hi & 0x1F) + 2;
                    for (int i = 0; i < count; ++i)
                    {
                        byte b = frame[(offset + i) & 0x7FF];
                        output[dst++] = b;
                        frame[frame_pos++] = b;
                        frame_pos &= 0x7FF;
                    }
                }
                ctl >>= 1;
            }
            return output;
        }
        
        void DecryptPs2 (byte[] data)
        {
            uint key = LittleEndian.ToUInt32 (data, 12);
            int shift = (int)(key >> 20) % 5 + 1;
            key = (key >> 24) + (key >> 3);
            for (int i = 0x30; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR ((byte)(key ^ (data[i] - 0x7Cu)), shift);
            }
        }

        void DecryptPb3 (byte[] data)
        {
            byte key1 = data[data.Length-3];
            byte key2 = data[data.Length-2];
            int src = data.Length - 0x2F;
            for (int i = 8; i < 0x34; i += 2)
            {
                data[i  ] ^= key1;
                data[i  ] -= data[src++];
                data[i+1] ^= key2;
                data[i+1] -= data[src++];
            }
		}
    }

    internal class Cpz5Decoder
    {
        protected byte[] m_decode_table = new byte[0x100];

        public Cpz5Decoder (uint key, uint summand, uint factor)
        {
            Init (key, summand, factor);
        }

        public void Init (uint key, uint summand, uint factor)
        {
            for (int i = 0; i < 0x100; ++i)
                m_decode_table[i] = (byte)i;

            for (int i = 0; i < 0x100; ++i)
            {
                uint i0 = (key >> 16) & 0xFF;
                uint i1 = key & 0xFF;
                var tmp = m_decode_table[i0];
                m_decode_table[i0] = m_decode_table[i1];
                m_decode_table[i1] = tmp;

                i0 = (key >> 8) & 0xFF;
                i1 = key >> 24;
                tmp = m_decode_table[i0];
                m_decode_table[i0] = m_decode_table[i1];
                m_decode_table[i1] = tmp;

                key = summand + factor * Binary.RotR (key, 2);
            }
        }

        public void Decode (byte[] data, int offset, int length, byte key)
        {
            for (int i = 0; i < length; ++i)
                data[offset+i] = m_decode_table[key ^ data[offset+i]];
        }

        public void DecryptEntry (byte[] data, uint[] cmvs_md5, uint seed)
        {
            if (null == data)
                throw new ArgumentNullException ("data");
            if (null == cmvs_md5 || cmvs_md5.Length < 4)
                throw new ArgumentException ("cmvs_md5");

            int secret_length = Math.Min (CpzOpener.CmvsScheme.Cpz5Secret.Length, 0x10) * 4;
            byte[] key_bytes  = new byte[secret_length];
            uint[] secret_key = new uint[0x10];

            uint key = cmvs_md5[1] >> 2;
            Buffer.BlockCopy (CpzOpener.CmvsScheme.Cpz5Secret, 0, key_bytes, 0, secret_length);
            for (int i = 0; i < secret_length; ++i)
                key_bytes[i] = (byte)(key ^ m_decode_table[key_bytes[i]]);

            Buffer.BlockCopy (key_bytes, 0, secret_key, 0, secret_length);
            for (int i = 0; i < secret_key.Length; ++i)		
                secret_key[i] ^= seed;

            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    key = 0x2547B39E;
                    int k = 9;
                    for (int i = data.Length / 4; i > 0; --i)
                    {	
                        *data32 = cmvs_md5[key & 3] ^ ((*data32 ^ secret_key[(key >> 6) & 0xf] ^ (secret_key[k] >> 1)) - seed);
                        k = (k + 1) & 0xf;
                        key += seed + *data32++;
                    }
                    byte* data8 = (byte*)data32;
                    for (int i = data.Length & 3; i > 0; --i)
                    {
                        *data8 = m_decode_table[*data8 ^ 0xCB];
                        ++data8;
                    }
                }
            }
        }
    }
}
