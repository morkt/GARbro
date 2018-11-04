//! \file       ArcAFA.cs
//! \date       Mon Apr 25 18:18:57 2016
//! \brief      AliceSoft System 4 engine resource archive.
//
// Copyright (C) 2016-2018 by morkt
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

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AfaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AFA"; } }
        public override string Description { get { return "AliceSoft System 4 resource archive"; } }
        public override uint     Signature { get { return 0x48414641; } } // 'AFAH'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (8, "AlicArch"))
                return TryOpenV3 (file);
            if (!file.View.AsciiEqual (0x1C, "INFO"))
                return null;
            int version = file.View.ReadInt32 (0x10);
            long base_offset = file.View.ReadUInt32 (0x18);
            uint packed_size = file.View.ReadUInt32 (0x20);
            int unpacked_size = file.View.ReadInt32 (0x24);
            int count = file.View.ReadInt32 (0x28);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            var name_buf = new byte[0x40];
            using (var input = file.CreateStream (0x2C, packed_size))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryReader (zstream))
            {
                for (int i = 0; i < count; ++i)
                {
                    int name_length = index.ReadInt32();
                    int index_step = index.ReadInt32();
                    if (name_length <= 0 || name_length > index_step || index_step > unpacked_size)
                        return null;
                    if (index_step > name_buf.Length)
                        name_buf = new byte[index_step];
                    if (index_step != index.Read (name_buf, 0, index_step))
                        return null;
                    var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    index.ReadInt32();
                    index.ReadInt32();
                    if (version < 2)
                        index.ReadInt32();
                    entry.Offset = index.ReadUInt32() + base_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        internal ArcFile TryOpenV3 (ArcView file)
        {
            if (file.View.ReadInt32 (8) != 3)
                return null;
            uint index_size = file.View.ReadUInt32 (4);
            var index = new AfaIndexReader (file, index_size);
            var dir = index.Read();
            if (null == dir || 0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        static readonly byte[] AffKey = {
            0xC8, 0xBB, 0x8F, 0xB7, 0xED, 0x43, 0x99, 0x4A,
            0xA2, 0x7E, 0x5B, 0xB0, 0x68, 0x18, 0xF8, 0x88
        };

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 0x10 || !arc.File.View.AsciiEqual (entry.Offset, "AFF\0"))
                return base.OpenEntry (arc, entry);
            uint data_size = entry.Size - 0x10u;
            uint encrypted_length = Math.Min (0x40u, data_size);
            var prefix = arc.File.View.ReadBytes (entry.Offset+0x10, encrypted_length);
            for (int i = 0; i < prefix.Length; ++i)
                prefix[i] ^= AffKey[i & 0xF];
            if (data_size <= 0x40)
                return new BinMemoryStream (prefix, entry.Name);
            var rest = arc.File.CreateStream (entry.Offset+0x10+encrypted_length, data_size-encrypted_length);
            return new PrefixStream (prefix, rest);
        }
    }

    internal sealed class AfaIndexReader
    {
        ArcView         m_file;
        uint            m_data_offset;
        byte[]          m_dict;

        public AfaIndexReader (ArcView file, uint index_size)
        {
            m_file = file;
            m_data_offset = index_size + 8;
        }

        public List<Entry> Read ()
        {
            byte[] packed;
            using (var input = m_file.CreateStream (12, m_data_offset-12))
            using (var bits = new MsbBitStream (input))
            {
                bits.GetNextBit();
                m_dict = ReadBytes (bits);
                if (null == m_dict)
                    return null;
                int packed_size   = ReadInt32 (bits);
                int unpacked_size = ReadInt32 (bits);
                packed = new byte[packed_size];
                for (int i = 0; i < packed_size; ++i)
                {
                    packed[i] = (byte)bits.GetBits (8);
                }
            }
            using (var bstr = new BinMemoryStream (packed))
            using (var zstr = new ZLibStream (bstr, CompressionMode.Decompress))
            using (var index = new MsbBitStream (zstr))
            {
                index.GetNextBit();
                int count = ReadInt32 (index);
                if (!ArchiveFormat.IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    if (index.GetBits (2) == -1)
                        break;
                    var name_buf = ReadEncryptedChars (index);
                    if (null == name_buf)
                        return null;
                    var name = DecryptString (name_buf, name_buf.Length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    ReadInt32 (index);
                    ReadInt32 (index);
                    entry.Offset = (uint)ReadInt32 (index) + m_data_offset;
                    entry.Size   = (uint)ReadInt32 (index);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return dir;
            }
        }

        byte[] ReadBytes (MsbBitStream input)
        {
            int buf_size = ReadInt32 (input);
            var buf = new byte[buf_size];
            int dst = 0;
            var rnd = new RandomGenerator ((uint)buf_size);
            while (dst < buf_size)
            {
                int count = (int)rnd.GetNext() & 3;
                int skipped = input.GetBits (count + 1);
                if (-1 == skipped)
                    return null;
                rnd.GetNext();

                int v = input.GetBits (8);
                if (-1 == v)
                    return null;
                buf[dst++] = (byte)v;
            }
            return buf;
        }

        ushort[] ReadEncryptedChars (MsbBitStream input)
        {
            int buf_size = ReadInt32 (input);
            var buf = new ushort[buf_size];
            int dst = 0;
            var rnd = new RandomGenerator ((uint)buf_size);
            while (dst < buf_size)
            {
                int count = (int)rnd.GetNext() & 3;
                int skipped = input.GetBits (count + 1);
                if (-1 == skipped)
                    return null;
                rnd.GetNext();

                int lo = input.GetBits (8);
                int hi = input.GetBits (8);
                if (-1 == lo || -1 == hi)
                    return null;
                buf[dst++] = (ushort)(lo | hi << 8);
            }
            return buf;
        }

        byte[] m_string_buf = new byte[0x100];

        string DecryptString (ushort[] input, int input_length)
        {
            if (m_string_buf.Length < input_length)
                m_string_buf = new byte[input_length];
            for (int i = 0; i < input_length; ++i)
            {
                m_string_buf[i] = (byte)(m_dict[input[i]] ^ 0xA4);
            }
            return Encodings.cp932.GetString (m_string_buf, 0, input_length);
        }

        static int ReadInt32 (MsbBitStream input)
        {
            int b0 = input.GetBits (8);
            int b1 = input.GetBits (8);
            int b2 = input.GetBits (8);
            int b3 = input.GetBits (8);
            return b3 << 24 | b2 << 16 | b1 << 8 | b0;
        }
    }

    internal class RandomGenerator
    {
        uint[]  m_state = new uint[521];
        int     m_current;

        public RandomGenerator (uint seed)
        {
            Init (seed);
        }

        public void Init (uint seed)
        {
            uint val = 0;
            for (int i = 0; i < 17; ++i)
            {
                for (int j = 0; j < 32; ++j)
                {
                    seed = 1566083941u * seed + 1;
                    val = seed & 0x80000000 | (val >> 1);
                }
                m_state[i] = val;
            }
            m_state[16] = m_state[15] ^ (m_state[0] >> 9) ^ (m_state[16] << 23);
            for (int i = 17; i < 521; ++i)
            {
                m_state[i] = m_state[i-1] ^ (m_state[i-16] >> 9) ^ (m_state[i-17] << 23);
            }
            Shuffle();
            Shuffle();
            Shuffle();
            Shuffle();
            m_current = -1;
        }

        public uint GetNext ()
        {
            ++m_current;
            if (m_current >= 521)
            {
                Shuffle();
                m_current = 0;
            }
            return m_state[m_current];
        }

        void Shuffle ()
        {
            for (int i = 0; i < 32; i += 4)
            {
                m_state[i  ] ^= m_state[i + 489];
                m_state[i+1] ^= m_state[i + 490];
                m_state[i+2] ^= m_state[i + 491];
                m_state[i+3] ^= m_state[i + 492];
            }
            for (int i = 32; i < 521; i += 3)
            {
                m_state[i  ] ^= m_state[i - 32];
                m_state[i+1] ^= m_state[i - 31];
                m_state[i+2] ^= m_state[i - 30];
            }
        }
    }
}
