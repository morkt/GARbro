//! \file       ArcCPZ.cs
//! \date       Tue Nov 24 11:27:23 2015
//! \brief      Purple Software resource archive.
//
// Copyright (C) 2015-2019 by morkt
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

namespace GameRes.Formats.Purple
{
    [Serializable]
    public class CmvsScheme
    {
        public int              Version;
        public uint[]           Cpz5Secret;
        public Cmvs.Md5Variant  Md5Variant;
        public uint             DecoderFactor;
        public uint             EntryInitKey;
        public uint             EntrySubKey = 0x5C29E87B;
        public byte             EntryTailKey;
        public byte             EntryKeyPos = 9;
        public uint             IndexSeed = 0x2A65CB4E;
        public uint             IndexAddend = 0x784C5962;
        public uint             IndexSubtrahend = 0x79;
        public uint[]           DirKeyAddend = DefaultDirKeyAddend;

        static readonly uint[] DefaultDirKeyAddend = { 0, 0x00112233, 0, 0x34258765 };
    }

    internal class CpzEntry : Entry
    {
        public uint CheckSum;
        public uint Key;
    }

    internal class CpzArchive : ArcFile
    {
        public CpzHeader    Header;
        public Cpz5Decoder  Decoder;
        public ArchiveKey   Key;

        public CpzArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, CpzHeader header, Cpz5Decoder decoder, ArchiveKey key)
            : base (arc, impl, dir)
        {
            Header = header;
            Decoder = decoder;
            Key = key;
        }
    }

    [Serializable]
    public class CpzScheme : ResourceScheme
    {
        public Dictionary<string, CmvsScheme> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class CpzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPZ"; } }
        public override string Description { get { return "CMVS engine resource archive"; } }
        public override uint     Signature { get { return 0x355A5043; } } // 'CPZ5'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public CpzOpener ()
        {
            Signatures = new uint[] { 0x355A5043, 0x365A5043, 0x375A5043 };
        }

        static CpzScheme DefaultScheme = new CpzScheme();

        internal Dictionary<string, CmvsScheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (CpzScheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (null == KnownSchemes)
                throw new OperationCanceledException ("Outdated encryption schemes database");

            var cpz = CpzHeader.Parse (file);
            if (null == cpz)
                return null;

            var index = file.View.ReadBytes (cpz.IndexOffset, cpz.IndexSize);
            if (!cpz.VerifyIndex (index))
                return null;

            int file_table_size = cpz.DirEntriesSize + cpz.FileEntriesSize;
            if (cpz.IndexKeySize > 24)
            {
                var index_key = UnpackIndexKey (index, file_table_size, cpz.IndexKeySize);
                for (int i = 0; i < file_table_size; ++i)
                {
                    index[i] ^= index_key[(i + 3) % 0x3FF];
                }
            }

            ArchiveKey arc_key = null;
            if (cpz.Version > 6)
                arc_key = FindArchiveKey (file.Name);
            if (null == arc_key)
                arc_key = new ArchiveKey();

            var index_copy = new CowArray<byte> (index, 0, file_table_size).ToArray();
            var cmvs_md5 = cpz.CmvsMd5.Clone() as uint[];
            foreach (var scheme in KnownSchemes.Values.Where (s => s.Version == cpz.Version))
            {
                var arc = ReadIndex (file, scheme, cpz, index, arc_key);
                if (null != arc)
                    return arc;
                // both CmvsMd5 and index was altered by ReadIndex in decryption attempt
                Array.Copy (cmvs_md5, cpz.CmvsMd5, 4);
                Array.Copy (index, index_copy, file_table_size);
            }
            throw new UnknownEncryptionScheme();
        }

        internal ArcFile ReadIndex (ArcView file, CmvsScheme scheme, CpzHeader cpz, byte[] index, ArchiveKey arc_key)
        {
            var cmvs_md5 = Cmvs.MD5.Create (scheme.Md5Variant);
            cmvs_md5.Compute (cpz.CmvsMd5);

            DecryptIndexStage1 (index, cpz.MasterKey ^ 0x3795B39A, scheme);

            var decoder = new Cpz5Decoder (scheme, cpz.MasterKey, cpz.CmvsMd5[1]);
            decoder.Decode (index, 0, cpz.DirEntriesSize, 0x3A);

            var key = new uint[4];
            key[0] = cpz.CmvsMd5[0] ^ (cpz.MasterKey + 0x76A3BF29);
            key[1] = cpz.CmvsMd5[1] ^  cpz.MasterKey;
            key[2] = cpz.CmvsMd5[2] ^ (cpz.MasterKey + 0x10000000);
            key[3] = cpz.CmvsMd5[3] ^  cpz.MasterKey;

            DecryptIndexDirectory (index, cpz.DirEntriesSize, key, arc_key.IndexDirKey);

            decoder.Init (cpz.MasterKey, cpz.CmvsMd5[2]);
            uint base_offset = cpz.IndexOffset + cpz.IndexSize;
            int dir_offset = 0;
            var dir = new List<Entry>();
            for (int i = 0; i < cpz.DirCount; ++i)
            {
                int dir_size = LittleEndian.ToInt32 (index, dir_offset);
                if (dir_size <= 0x10 || dir_size > index.Length)
                    return null;
                int  file_count     = LittleEndian.ToInt32 (index, dir_offset+4);
                if (file_count >= 0x10000)
                    return null;
                int  entries_offset = LittleEndian.ToInt32 (index, dir_offset+8);
                uint dir_key        = LittleEndian.ToUInt32 (index, dir_offset+0xC);
                var  dir_name       = Binary.GetCString (index, dir_offset+0x10, dir_size-0x10);

                int next_entries_offset;
                if (i + 1 == cpz.DirCount)
                    next_entries_offset = cpz.FileEntriesSize;
                else
                    next_entries_offset = LittleEndian.ToInt32 (index, dir_offset + dir_size + 8);

                int cur_entries_size = next_entries_offset - entries_offset;
                if (cur_entries_size <= 0)
                    return null;

                int cur_offset = cpz.DirEntriesSize + entries_offset;
                int cur_entries_end = cur_offset + cur_entries_size;
                decoder.Decode (index, cur_offset, cur_entries_size, 0x7E);

                for (int j = 0; j < 4; ++j)
                    key[j] = cpz.CmvsMd5[j] ^ (dir_key + scheme.DirKeyAddend[j]);

                DecryptIndexEntry (index, cur_offset, cur_entries_size, key, scheme.IndexSeed, arc_key.IndexEntryKey);
                bool is_root_dir = dir_name == "root";

                dir.Capacity = dir.Count + file_count;
                for (int j = 0; j < file_count; ++j)
                {
                    int entry_size = LittleEndian.ToInt32 (index, cur_offset);
                    if (entry_size > index.Length || entry_size <= cpz.EntryNameOffset)
                        return null;
                    int name_offset = cur_offset + cpz.EntryNameOffset;
                    var name = Binary.GetCString (index, name_offset, cur_entries_end - name_offset);
                    if (!is_root_dir)
                        name = Path.Combine (dir_name, name);
                    var entry = FormatCatalog.Instance.Create<CpzEntry> (name);
                    entry.Offset    = LittleEndian.ToInt64 (index, cur_offset+4) + base_offset;
                    entry.Size      = LittleEndian.ToUInt32 (index, cur_offset+0xC);
                    int key_offset = cur_offset + 0x10;
                    if (cpz.IsLongSize)
                        key_offset += 4;
                    entry.CheckSum  = LittleEndian.ToUInt32 (index, key_offset);
                    entry.Key       = LittleEndian.ToUInt32 (index, key_offset+4) + dir_key;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    cur_offset += entry_size;
                }
                dir_offset += dir_size;
            }
            if (cpz.IsEncrypted)
                decoder.Init (cpz.CmvsMd5[3], cpz.MasterKey);
            return new CpzArchive (file, this, dir, cpz, decoder, arc_key);
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
                uint key = (carc.Header.MasterKey ^ cent.Key) + (uint)carc.Header.DirCount;
                key ^= carc.Key.EntryDataKey2;
                key -= carc.Decoder.Scheme.EntrySubKey;
                key ^= carc.Header.EntryKey + carc.Key.EntryDataKey1;
                carc.Decoder.DecryptEntry (data, carc.Header.CmvsMd5, key);
            }
            if (data.Length > 0x30 && Binary.AsciiEqual (data, 0, "PS2A"))
                data = UnpackPs2 (data);
            else if (data.Length > 0x40 && Binary.AsciiEqual (data, 0, "PB3B"))
                DecryptPb3 (data);
            return new BinMemoryStream (data, entry.Name);
        }

        internal byte[] UnpackIndexKey (byte[] data, int offset, int length)
        {
            int key_offset = offset + 20;
            int packed_offset = offset + 24;
            int packed_length = length - 24;
            for (int i = 0; i < packed_length; ++i)
            {
                data[packed_offset + i] ^= data[key_offset + (i & 3)];
            }
            int unpacked_length = data.ToInt32 (offset + 16);
            var output = new byte[unpacked_length];
            var decoder = new HuffmanDecoder (data, packed_offset, packed_length, output);
            return decoder.Unpack();
        }

        void DecryptIndexStage1 (byte[] data, uint key, CmvsScheme scheme)
        {
            var secret = scheme.Cpz5Secret;
            var secret_key = new uint[24];
            int secret_length = Math.Min (24, secret.Length);
            for (int i = 0; i < secret_length; ++i)
                secret_key[i] = secret[i] - key;

            int shift = (int)(((key >> 24) ^ (key >> 16) ^ (key >> 8) ^ key ^ 0xB) & 0xF) + 7;
            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    int i = 5;
                    for (int n = data.Length / 4; n > 0; --n)
                    {
                        *data32 = Binary.RotR ((secret_key[i] ^ *data32) + scheme.IndexAddend, shift) + 0x01010101;
                        ++data32;
                        i = (i + 1) % 24;
                    }
                    byte* data8 = (byte*)data32;
                    for (int n = data.Length & 3; n > 0; --n)
                    {
                        *data8 = (byte)((*data8 ^ (secret_key[i] >> (n * 4))) - scheme.IndexSubtrahend);
                        ++data8;
                        i = (i + 1) % 24;
                    }
                }
            }
        }

        void EncryptIndexStage1 (byte[] data, uint key, CmvsScheme scheme)
        {
            var secret = scheme.Cpz5Secret;
            var secret_key = new uint[24];
            int secret_length = Math.Min(24, secret.Length);
            for (int i = 0; i < secret_length; ++i)
                secret_key[i] = secret[i] - key;

            int shift = (int)(((key >> 24) ^ (key >> 16) ^ (key >> 8) ^ key ^ 0xB) & 0xF) + 7;
            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    int i = 5;
                    for (int n = data.Length / 4; n > 0; --n)
                    {
                        *data32 = (Binary.RotL((*data32 - 0x01010101), shift) - scheme.IndexAddend) ^ secret_key[i];
                        ++data32;
                        i = (i + 1) % 24;
                    }
                    byte* data8 = (byte*)data32;
                    for (int n = data.Length & 3; n > 0; --n)
                    {
                        *data8 = (byte)((*data8 + scheme.IndexSubtrahend) ^ (secret_key[i] >> (n * 4)));
                        ++data8;
                        i = (i + 1) % 24;
                    }
                }
            }
        }

        void DecryptIndexDirectory (byte[] data, int length, uint[] key, uint arc_key)
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
                        seed += 0x10FB562Au ^ arc_key;
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

        void EncryptIndexDirectory (byte[] data, int length, uint[] key)
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
                        *data32 = (Binary.RotR(*data32 + seed, 3) + 0x4A91C262) ^ key[i & 3];
                        ++data32;
                        seed += 0x10FB562A;
                    }
                    byte* data8 = (byte*)data32;
                    for (int j = length & 3; j > 0; --j)
                    {
                        *data8 = (byte)((*data8 - 0x37) ^ (key[i++ & 3] >> 6));
                        ++data8;
                    }
                }
            }
        }

        void DecryptIndexEntry (byte[] data, int offset, int length, uint[] key, uint seed, uint arc_key)
        {
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException ("offset");
            if (length < 0 || length > data.Length || length > data.Length-offset)
                throw new ArgumentException ("length");
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
                        seed -= 0x139FA9B ^ arc_key;
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

        void EncryptIndexEntry (byte[] data, int offset, int length, uint[] key, uint seed)
        {
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || length > data.Length || length > data.Length - offset)
                throw new ArgumentException("length");
            unsafe
            {
                fixed (byte* raw = &data[offset])
                {
                    uint* data32 = (uint*)raw;
                    int i;
                    for (i = 0; i < length / 4; ++i)
                    {
                        *data32 = (Binary.RotR((*data32 - 0x37A19E8B), 2) + seed) ^ key[i & 3];
                        ++data32;
                        seed -= 0x139FA9B;
                    }
                    byte* data8 = (byte*)data32;
                    for (int j = length & 3; j > 0; --j)
                    {
                        *data8 = (byte)((*data8 - 5) ^ (key[i++ & 3] >> 4));
                        ++data8;
                    }
                }
            }
        }

        byte[] UnpackPs2 (byte[] data)
        {
            DecryptPs2 (data);
            return UnpackLzss (data);
        }

        internal static byte[] UnpackLzss (byte[] data)
        {
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

        void EncryptPb3 (byte[] data)
        {
            byte key1 = data[data.Length - 3];
            byte key2 = data[data.Length - 2];
            int src = data.Length - 0x2F;
            for (int i = 8; i < 0x34; i += 2)
            {
                data[i] += data[src++];
                data[i] ^= key1;
                data[i + 1] += data[src++];
                data[i + 1] ^= key2;
            }
        }

        ArchiveKey FindArchiveKey (string arc_name)
        {
            // look for "start.ps3" in the same directory as an archive
            var start_name = VFS.ChangeFileName (arc_name, "start.ps3");
            if (!VFS.FileExists (start_name))
                return null;
            byte[] start_data;
            using (var start = VFS.OpenView (start_name))
            {
                if (!start.View.AsciiEqual (0, "PS2A"))
                    return null;
                start_data = start.View.ReadBytes (0, (uint)start.MaxOffset);
            }
            arc_name = Path.GetFileName (arc_name);
            start_data = UnpackPs2 (start_data);

            int table_count = start_data.ToInt32 (0x10);
            int strings_offset = 0x30 + table_count * 4 + start_data.ToInt32 (0x14);
            int strings_size = start_data.ToInt32 (0x1C);
            if (strings_offset < 0x30 || strings_offset + strings_size > start_data.Length)
                return null;

            // search strings table for archive name
            int string_pos = strings_offset;
            int strings_end = strings_offset + strings_size;
            int arc_id = -1;
            while (string_pos < strings_end)
            {
                int end_pos = Array.IndexOf<byte> (start_data, 0, string_pos);
                if (-1 == end_pos)
                    end_pos = strings_offset + strings_size;
                if (end_pos != string_pos)
                {
                    var text = Encodings.cp932.GetString (start_data, string_pos, end_pos - string_pos);
                    if (VFS.IsPathEqualsToFileName (text, arc_name))
                    {
                        arc_id = string_pos - strings_offset;
                        break;
                    }
                }
                string_pos = end_pos + 1;
            }
            if (-1 == arc_id)
                return null;

            // search bytecode for a reference to archive name found above
            var id_bytes = new byte[4];
            LittleEndian.Pack (arc_id, id_bytes, 0);
            for (int data_pos = 0x30 + table_count * 4; data_pos + 4 <= strings_offset; ++data_pos)
            {
                if (start_data[data_pos+0] == id_bytes[0] && start_data[data_pos+1] == id_bytes[1] &&
                    start_data[data_pos+2] == id_bytes[2] && start_data[data_pos+3] == id_bytes[3])
                {
                    if (start_data[data_pos-0x33] == 2 && start_data[data_pos-0x32] == 0 &&
                        start_data[data_pos-0x31] == 1)
                    {
                        return new ArchiveKey {
                            IndexDirKey   = start_data.ToUInt32 (data_pos-0x0C),
                            IndexEntryKey = start_data.ToUInt32 (data_pos-0x18),
                            EntryDataKey1 = start_data.ToUInt32 (data_pos-0x24),
                            EntryDataKey2 = start_data.ToUInt32 (data_pos-0x30),
                        };
                    }
                }
            }
            return null;
        }
    }

    internal class Cpz5Decoder
    {
        protected byte[]        m_decode_table = new byte[0x100];
        protected CmvsScheme    m_scheme;

        public CmvsScheme Scheme { get { return m_scheme; } }

        public Cpz5Decoder (CmvsScheme scheme, uint key, uint summand)
        {
            m_scheme = scheme;
            Init (key, summand);
        }

        public void Init (uint key, uint summand)
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

                key = summand + m_scheme.DecoderFactor * Binary.RotR (key, 2);
            }
        }

        public void Decode (byte[] data, int offset, int length, byte key)
        {
            for (int i = 0; i < length; ++i)
                data[offset+i] = m_decode_table[key ^ data[offset+i]];
        }

        public void Encode (byte[] data, int offset, int length, byte key)
        {
            for (int i = 0; i < length; ++i)
            {
                for (int s = 0; s < m_decode_table.Length; s++)
                {
                    if (data[offset+i] == m_decode_table[s])
                    {
                        data[offset+i] = (byte)(key ^ s);
                        break;
                    }
                }
            }
        }

        public void DecryptEntry (byte[] data, uint[] cmvs_md5, uint seed)
        {
            if (null == data)
                throw new ArgumentNullException ("data");
            if (null == cmvs_md5 || cmvs_md5.Length < 4)
                throw new ArgumentException ("cmvs_md5");

            int secret_length = Math.Min (m_scheme.Cpz5Secret.Length, 0x10) * sizeof(uint);
            byte[] key_bytes  = new byte[secret_length];

            uint key = cmvs_md5[1] >> 2;
            Buffer.BlockCopy (m_scheme.Cpz5Secret, 0, key_bytes, 0, secret_length);
            for (int i = 0; i < secret_length; ++i)
                key_bytes[i] = (byte)(key ^ m_decode_table[key_bytes[i]]);

            uint[] secret_key = new uint[0x10];
            Buffer.BlockCopy (key_bytes, 0, secret_key, 0, secret_length);
            for (int i = 0; i < secret_key.Length; ++i)		
                secret_key[i] ^= seed;

            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    key = m_scheme.EntryInitKey;
                    int k = m_scheme.EntryKeyPos;
                    for (int i = data.Length / 4; i > 0; --i)
                    {	
                        *data32 = cmvs_md5[key & 3] ^ ((*data32 ^ secret_key[(key >> 6) & 0xf] ^ (secret_key[k] >> 1)) - seed);
                        k = (k + 1) & 0xf;
                        key += seed + *data32++;
                    }
                    byte* data8 = (byte*)data32;
                    for (int i = data.Length & 3; i > 0; --i)
                    {
                        *data8 = m_decode_table[*data8 ^ m_scheme.EntryTailKey];
                        ++data8;
                    }
                }
            }
        }

        public void EncryptEntry (byte[] data, uint[] cmvs_md5, uint seed)
        {
            if (null == data)
                throw new ArgumentNullException("data");
            if (null == cmvs_md5 || cmvs_md5.Length < 4)
                throw new ArgumentException("cmvs_md5");

            int secret_length = Math.Min(m_scheme.Cpz5Secret.Length, 0x10) * sizeof(uint);
            byte[] key_bytes = new byte[secret_length];

            uint key = cmvs_md5[1] >> 2;
            Buffer.BlockCopy(m_scheme.Cpz5Secret, 0, key_bytes, 0, secret_length);
            for (int i = 0; i < secret_length; ++i)
                key_bytes[i] = (byte)(key ^ m_decode_table[key_bytes[i]]);

            uint[] secret_key = new uint[0x10];
            Buffer.BlockCopy(key_bytes, 0, secret_key, 0, secret_length);
            for (int i = 0; i < secret_key.Length; ++i)
                secret_key[i] ^= seed;

            unsafe
            {
                fixed (byte* raw = data)
                {
                    uint* data32 = (uint*)raw;
                    key = m_scheme.EntryInitKey;
                    int k = m_scheme.EntryKeyPos;
                    for (int i = data.Length / 4; i > 0; --i)
                    {
                        uint backup = *data32;
                        *data32 = (((cmvs_md5[key & 3] ^ *data32) + seed) ^ (secret_key[k] >> 1)) ^ secret_key[(key >> 6) & 0xf];
                        k = (k + 1) & 0xf;
                        key += seed + backup;
                        ++data32;
                    }
                    byte* data8 = (byte*)data32;
                    for (int i = data.Length & 3; i > 0; --i)
                    {
                        for (int s = 0; s < m_decode_table.Length; s++)
                        {
                            if (*data8 == m_decode_table[s])
                            {
                                *data8 = (byte)(s ^ m_scheme.EntryTailKey);
                                break;
                            }
                        }
                        ++data8;
                    }
                }
            }
        }
    }

    [Serializable]
    public class ArchiveKey
    {
        public uint IndexDirKey;
        public uint IndexEntryKey;
        public uint EntryDataKey1;
        public uint EntryDataKey2;
    }
}
