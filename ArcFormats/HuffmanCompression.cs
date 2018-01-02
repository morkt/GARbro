//! \file       HuffmanCompression.cs
//! \brief      Huffman-compressed streams.
//
// C# implementation Copyright (C) 2014-2018 by morkt
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
using System.IO;
using GameRes.Formats;

namespace GameRes.Compression
{
    public class HuffmanStream : PackedStream<HuffmanDecompressor>
    {
        public HuffmanStream (Stream input, bool leave_open = false) : base (input, leave_open)
        {
        }
    }

    public class HuffmanDecompressor : Decompressor
    {
        MsbBitStream        m_input;

        public override void Initialize (Stream input)
        {
            m_input = new MsbBitStream (input, true);
        }

        const int TreeSize = 512;

        ushort[] lhs = new ushort[TreeSize];
        ushort[] rhs = new ushort[TreeSize];
        ushort m_token = 256;

        protected override IEnumerator<int> Unpack ()
        {
            m_token = 256;
            ushort root = CreateTree();
            for (;;)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    int bit = m_input.GetBits (1);
                    if (-1 == bit)
                        yield break;
                    if (bit != 0)
                        symbol = rhs[symbol];
                    else
                        symbol = lhs[symbol];
                }
                m_buffer[m_pos++] = (byte)symbol;
                if (0 == --m_length)
                    yield return m_pos;
            }
        }

        ushort CreateTree ()
        {
            int bit = m_input.GetBits (1);
            if (-1 == bit)
            {
                throw new EndOfStreamException ("Unexpected end of the Huffman-compressed stream.");
            }
            else if (bit != 0)
            {
                ushort v = m_token++;
                if (v >= TreeSize)
                    throw new InvalidFormatException ("Invalid Huffman-compressed stream.");
                lhs[v] = CreateTree();
                rhs[v] = CreateTree();
                return v;
            }
            else
            {
                return (ushort)m_input.GetBits (8);
            }
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    public class HuffmanDecoder
    {
        byte[] m_src;
        byte[] m_dst;
        int m_input_pos;
        int m_remaining;

        public HuffmanDecoder (byte[] src, int index, int length, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            m_input_pos = index;
            m_remaining = length;
        }

        public HuffmanDecoder (byte[] src, byte[] dst) : this (src, 0, src.Length, dst)
        {
        }

        public byte[] Unpack ()
        {
            using (var packed = new BinMemoryStream (m_src, m_input_pos, m_remaining))
            using (var hstr = new HuffmanStream (packed))
            {
                hstr.Read (m_dst, 0, m_dst.Length);
                return m_dst;
            }
        }
    }
}
