//! \file       ArcMPK.cs
//! \date       2017 Dec 19
//! \brief      Kurumi resource archive.
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
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Kurumi
{
    [Export(typeof(ArchiveFormat))]
    public class MpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPK/KURUMI"; } }
        public override string Description { get { return "Kurumi resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MP"))
                return null;
            int version = file.View.ReadByte (2);
            if (version > 1)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            if (data_offset <= 12 || data_offset >= file.MaxOffset)
                return null;
            using (var index = Decompress (file, 12, data_offset-12))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0xF8);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = index.ReadUInt32() + data_offset;
                    entry.Size   = index.ReadUInt32();
                    entry.UnpackedSize = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = true;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            return Decompress (arc.File, entry.Offset, entry.Size).AsStream;
        }

        IBinaryStream Decompress (ArcView file, long offset, uint packed_size)
        {
            uint unpacked_size = Binary.BigEndian (file.View.ReadUInt32 (offset));
            bool is_packed = file.View.ReadByte (offset+8) != 0;
            if (!is_packed)
                return file.CreateStream (offset+9, unpacked_size);
            using (var input = file.CreateStream (offset+9, packed_size-9))
            {
                var compr = new MpkCompression (input, (int)unpacked_size);
                var data = compr.Unpack();
                return new BinMemoryStream (data);
            }
        }
    }

    internal sealed class MpkCompression
    {
        IBinaryStream   m_input;
        byte[]          m_output;

        public MpkCompression (IBinaryStream input, int unpacked_size)
        {
            m_input = input;
            m_output = new byte[unpacked_size];
        }

        public byte[] Unpack ()
        {
            var data = CreateBuffer();
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count = sub_429D50();
                if (0 == count)
                    break;
                Buffer.BlockCopy (data, 0, m_output, dst, count);
                dst += count;
            }
            return m_output;
        }

        byte[] CreateBuffer ()
        {
            word_471CEC = 16;
            if (null == m_buffer)
            {
                dword_471D04 = new ushort[0x1000];
                dword_471D54 = new ushort[0x100];
                InitTree (15);
                m_buffer = new byte[m_buffer_size];
            }
            word_471D6C = 0;
            m_eof = false;
            InitBits();
            return m_buffer;
        }

        int     m_bits_avail;
        int     m_bits;
        bool    m_eof;

        int     word_471CE8;
        int     m_buffer_size;
        ushort  word_471CEC;
        short   word_471D00;
        short   word_471D4E;
        int     word_471D6C;
        ushort  word_471D58;
        ushort  m_tree_size;
        ushort  word_471D60;

        byte[]      m_buffer;
        ushort[]    m_lhs_nodes;
        ushort[]    m_rhs_nodes;
        byte[]      dword_471CFC;
        ushort[]    dword_471D04;
        ushort[]    dword_471D54;
        byte[]      dword_471D64;

        void InitTree (short depth)
        {
            word_471D4E = depth;
            m_tree_size = 0x1FE;
            word_471D58 = (ushort)(depth + 1);
            m_buffer_size = 1 << depth;
            dword_471D64 = new byte[0x1FE];
            dword_471CFC = new byte[Math.Max (0x13, depth + 1)];
            m_lhs_nodes = new ushort[2 * m_tree_size - 1];
            m_rhs_nodes = new ushort[2 * m_tree_size - 1];
        }

        int InitBits ()
        {
            m_bits_avail = 0;
            m_bits = 0;
            word_471D60 = 0;
            word_471D00 = 0;
            return LoadBits (word_471CEC);
        }

        short GetBits (short a1)
        {
            int v1 = word_471D60 >> (word_471CEC - a1);
            LoadBits (a1);
            return (short)v1;
        }

        ushort LoadBits (int count)
        {
            word_471D60 <<= count;
            if (count > m_bits_avail)
            {
                do
                {
                    count -= m_bits_avail;
                    word_471D60 |= (ushort)(m_bits << count);
                    int bits = m_input.ReadByte();
                    if (-1 == bits)
                    {
                        bits = 0;
                        m_eof = true;
                    }
                    m_bits = bits;
                    m_bits_avail = 8;
                }
                while (count > 8);
            }
            int v = m_bits_avail - count;
            ushort result = (ushort)(m_bits >> v);
            m_bits_avail = v;
            word_471D60 |= result;
            return result;
        }

        int sub_429D50 ()
        {
            int dst = 0;
            while (--word_471D6C >= 0)
            {
                m_buffer[dst++ & 0xFFFF] = m_buffer[word_471CE8];
                word_471CE8 = (word_471CE8 + 1) & (m_buffer_size - 1);
                if (dst == m_buffer_size)
                    return dst;
            }
            while (!m_eof || word_471D00 != 0)
            {
                ushort v4 = sub_42A090();
                if (v4 > 0xFF)
                {
                    short offset = sub_42A490();
                    word_471D6C = v4 - 254;
                    word_471CE8 = (dst - offset - 1) & (m_buffer_size - 1);
                    if (word_471D6C >= 0)
                    {
                        do
                        {
                            m_buffer[dst++ & 0xFFFF] = m_buffer[word_471CE8];
                            word_471CE8 = (word_471CE8 + 1) & (m_buffer_size - 1);
                            if (dst == m_buffer_size)
                                return dst;
                        }
                        while (--word_471D6C >= 0);
                    }
                }
                else
                {
                    m_buffer[dst++ & 0xFFFF] = (byte)v4;
                    if (dst == m_buffer_size)
                        break;
                }
            }
            return dst;
        }

        ushort sub_42A090 ()
        {
            if (0 == word_471D00)
            {
                word_471D00 = GetBits (16);
                sub_42A1A0 (19, 5, 3);
                sub_42A2E0();
                sub_42A1A0 (word_471D58, 4, 0xFFFF);
            }
            --word_471D00;
            ushort v1 = dword_471D04[word_471D60 >> (word_471CEC - 12)];
            int v2 = 1 << (word_471CEC - 13);
            while (v1 >= m_tree_size)
            {
                if (0 != (word_471D60 & v2))
                    v1 = m_rhs_nodes[v1];
                else
                    v1 = m_lhs_nodes[v1];
                v2 >>= 1;
            }
            LoadBits (dword_471D64[v1]);
            return v1;
        }

        void sub_42A1A0 (int a1, short a2, ushort a3)
        {
            short v3 = GetBits (a2);
            if (0 == v3)
            {
                short v5 = GetBits (a2);
                for (int i = 0; i < a1; ++i)
                {
                    dword_471CFC[i] = 0;
                }
                for (int i = 0; i < 256; ++i)
                {
                    dword_471D54[i] = (ushort)v5;
                }
                return;
            }
            for (int v10 = 0; v10 < v3; )
            {
                int v11 = word_471D60 >> (word_471CEC - 3);
                if (v11 == 7)
                {
                    int v14 = 1 << (word_471CEC - 4);
                    while (0 != (word_471D60 & v14))
                    {
                        v14 >>= 1;
                        ++v11;
                    }
                    LoadBits (v11 - 3);
                }
                else if (v11 < 7)
                {
                    LoadBits (3);
                }
                else
                    LoadBits (v11 - 3);
                dword_471CFC[v10++] = (byte)v11;
                if (v10 == a3)
                {
                    for (int count = GetBits (2); count > 0; --count)
                    {
                        dword_471CFC[v10++] = 0;
                    }
                }
            }
            int n = v3;
            for (int count = a1 - v3; count > 0; --count)
            {
                dword_471CFC[n++] = 0;
            }
            sub_42A540 (a1, dword_471CFC, 8, dword_471D54);
        }

        void sub_42A2E0 ()
        {
            short v0 = GetBits(9);
            if (v0 != 0)
            {
                int v8 = 0;
                while (v8 < v0)
                {
                    int v9 = dword_471D54[word_471D60 >> (word_471CEC - 8)];
                    int v10 = 1 << (word_471CEC - 9);
                    while (v9 >= 19)
                    {
                        if (0 != (word_471D60 & v10))
                            v9 = m_rhs_nodes[v9];
                        else
                            v9 = m_lhs_nodes[v9];
                        v10 >>= 1;
                    }
                    LoadBits (dword_471CFC[v9]);
                    if (v9 <= 2)
                    {
                        int count;
                        if (0 == v9)
                            count = 1;
                        else if (1 == v9)
                            count = GetBits (4) + 3;
                        else
                            count = GetBits (9) + 20;
                        while (count --> 0)
                        {
                            dword_471D64[v8++] = 0;
                        }
                    }
                    else
                    {
                        dword_471D64[v8++] = (byte)(v9 - 2);
                    }
                }
                while (v8 < m_tree_size)
                {
                    dword_471D64[v8++] = 0;
                }
                sub_42A540 (m_tree_size, dword_471D64, 12, dword_471D04);
            }
            else
            {
                short v3 = GetBits (9);
                for (int i = 0; i < m_tree_size; ++i)
                {
                    dword_471D64[i] = 0;
                }
                for (int i = 0; i < 0x1000; ++i)
                {
                    dword_471D04[i] = (ushort)v3;
                }
            }
        }

        short sub_42A490()
        {
            var v0 = dword_471D54[word_471D60 >> (word_471CEC - 8)];
            int v1 = 1 << (word_471CEC - 9);
            while (v0 >= word_471D58)
            {
                if (0 != (word_471D60 & v1))
                    v0 = m_rhs_nodes[v0];
                else
                    v0 = m_lhs_nodes[v0];
                v1 >>= 1;
            }
            LoadBits (dword_471CFC[v0]);
            if (0 == v0)
                return 0;
            short v2 = GetBits ((short)(v0 - 1));
            return (short)((1 << (v0 - 1)) + v2);
        }

        ushort[] v33 = new ushort[36];

        int sub_42A540 (int a1, byte[] a2, ushort a3, ushort[] a4)
        {
            for (int i = 1; i <= 0x10; ++i)
                v33[i] = 0;
            for (int i = 0; i < a1; ++i)
                ++v33[a2[i]];

            v33[19] = 0;
            int v6 = 15;
            for (int v7 = 0; v7 < 16; ++v7)
            {
                int v10 = v33[v7 + 1] << v6--;
                v33[v7 + 20] = (ushort)(v33[v7 + 19] + v10);
            }
            if (v33[35] != 0)
                throw new InvalidFormatException();
            int v11 = 1;
            int v12 = 16 - a3;
            while (v11 <= a3)
            {
                v33[v11 + 18] = (ushort)(v33[v11 + 18] >> v12);
                v33[v11] = (ushort)(1 << (a3 - v11));
                ++v11;
            }
            if (v11 <= 0x10)
            {
                int v14 = v11;
                int v15 = 17 - v11;
                int v16 = v11; // within v33
                do
                {
                    v11 = 1 << (16 - v14++);
                    v33[v16++] = (ushort)v11;
                    --v15;
                }
                while (v15 > 0);
            }
            v11 = v33[a3 + 19] >> v12;
            if (v11 != 0)
            {
                while (v11 != (1 << a3))
                {
                    a4[v11++] = 0;
                }
            }
            int v18 = a1;
            int v19 = 0;
            int result = 0;
            while (v19 < a1)
            {
                int v21 = a2[result];
                if (v21 != 0)
                {
                    int v32 = v21 + 18; // within v33
                    int v22 = v33[v32];
                    int v24 = v22 + v33[v21];
                    if (v21 > a3)
                    {
                        int v23 = v22;
                        var v26 = new ArrayPtr<ushort> (a4, v22 >> (16 - a3));
                        for (int count = v21 - a3; count != 0; --count)
                        {
                            if (0 == v26.Value)
                            {
                                m_lhs_nodes[v18] = 0;
                                m_rhs_nodes[v18] = 0;
                                v26.Value = (ushort)v18;
                                ++v18;
                            }
                            if (0 != ((1 << (15 - a3)) & v23))
                                v26 = new ArrayPtr<ushort> (m_rhs_nodes, v26.Value);
                            else
                                v26 = new ArrayPtr<ushort> (m_lhs_nodes, v26.Value);
                            v23 <<= 1;
                        }
                        v26.Value = (ushort)v19;
                    }
                    else
                    {
                        while (v22 < v24)
                        {
                            a4[v22++] = (ushort)v19;
                        }
                    }
                    v33[v32] = (ushort)v24;
                }
                result = ++v19;
            }
            return result;
        }
    }

    internal struct ArrayPtr<T>
    {
        T[]     m_array;
        int     m_index;

        public T Value
        {
            get { return m_array[m_index]; }
            set { m_array[m_index] = value; }
        }

        public ArrayPtr (T[] array, int index)
        {
            m_array = array;
            m_index = index;
        }
    }
}
