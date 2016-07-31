//! \file       ImageTINK.cs
//! \date       Fri Jun 17 18:49:04 2016
//! \brief      Tinker Bell encrypted image file.
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

using System;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Cyberworks
{
    public enum AImageHeader
    {
        Flags           = 0,
        Field1          = 1,
        Field2          = 2,
        Height          = 3,
        Width           = 4,
        UnpackedSize    = 5,
        AlphaSize       = 6,
        BitsSize        = 7,
        Ignored         = Field1,
    }

    internal sealed class AImageReader : IDisposable
    {
        public readonly ImageMetaData Info = new ImageMetaData();

        BinaryReader    m_input;
        byte[]          m_output;
        AImageScheme    m_scheme;

        public byte[] Data { get { return m_output; } }

        public AImageReader (Stream input, AImageScheme scheme)
        {
            m_input = new ArcView.Reader (input);
            m_scheme = scheme;
        }

        public void Unpack ()
        {
            int header_length = Math.Max (8, m_scheme.HeaderOrder.Length);
            var header = new int[header_length];
            for (int i = 0; i < m_scheme.HeaderOrder.Length; ++i)
            {
                int b = GetInt();
                header[m_scheme.HeaderOrder[i]] = b;
            }
            Info.Width  = (uint)header[4];
            Info.Height = (uint)header[3];
            if (0 == Info.Width || Info.Width >= 0x8000 || 0 == Info.Height || Info.Height >= 0x8000)
                throw new InvalidFormatException();
            int unpacked_size = header[5];
            if (unpacked_size <= 0)
                throw new InvalidFormatException();
            int flags     = header[0];
            int bits_size = header[7];
            int data_offset = bits_size * 2;
            if (0 == flags)
                CopyV0 (unpacked_size);
            else if (2 == (flags & 6))
                UnpackV2 (bits_size, data_offset);
            else if (6 == (flags & 6))
            {
                if (0 == bits_size)
                    CopyV6 (unpacked_size, header[6]);
                else
                    UnpackV6 (bits_size, data_offset, data_offset + header[6]);
            }
            else
                throw new InvalidFormatException();
        }

        void CopyV0 (int data_size)
        {
            int plane_size = (int)Info.Width * (int)Info.Height;
            if (plane_size == data_size)
            {
                Info.BPP = 8;
                m_output = m_input.ReadBytes (data_size);
            }
            else if (3 * plane_size == data_size)
            {
                Info.BPP = 24;
                m_output = m_input.ReadBytes (data_size);
            }
            else if (4 * plane_size == data_size)
            {
                Info.BPP = 32;
                m_output = m_input.ReadBytes (data_size);
            }
            else
            {
                Info.BPP = 24;
                int dst_stride = (int)Info.Width * 3;
                int src_stride = (dst_stride + 3) & ~3;
                if (src_stride * (int)Info.Height != data_size)
                    throw new InvalidFormatException();
                m_output = new byte[dst_stride * (int)Info.Height];
                var gap = new byte[src_stride-dst_stride];
                int dst = 0;
                for (uint y = 0; y < Info.Height; ++y)
                {
                    m_input.Read (m_output, dst, dst_stride);
                    m_input.Read (gap, 0, gap.Length);
                    dst += dst_stride;
                }
            }
        }

        void UnpackV2 (int offset1, int rgb_offset)
        {
            Info.BPP = 24;
            var rgb_map = m_input.ReadBytes (offset1);
            var alpha_map = m_input.ReadBytes (rgb_offset-offset1);
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 3];

            int bit = 1;
            int bit_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                if ((bit & alpha_map[bit_src]) == 0 && (bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                }
                dst += 3;
                if (0x80 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
                else
                    bit <<= 1;
            }
        }

        void CopyV6 (int alpha_size, int rgb_size)
        {
            Info.BPP = 32;
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 4];
            int stride = ((int)Info.Width * 3 + 3) & ~3;
            var line = new byte[stride];
            int dst = 3;
            for (uint y = 0; y < Info.Height; ++y)
            {
                m_input.Read (line, 0, stride);
                int src = 0;
                for (uint x = 0; x < Info.Width; ++x)
                {
                    m_output[dst] = line[src];
                    dst += 4;
                    src += 3;
                }
            }
            dst = 0;
            for (uint y = 0; y < Info.Height; ++y)
            {
                m_input.Read (line, 0, stride);
                int src = 0;
                for (uint x = 0; x < Info.Width; ++x)
                {
                    m_output[dst  ] = line[src++];
                    m_output[dst+1] = line[src++];
                    m_output[dst+2] = line[src++];
                    dst += 4;
                }
            }
        }

        void UnpackV6 (int offset1, int alpha_offset, int rgb_offset)
        {
            Info.BPP = 32;
            var rgb_map = m_input.ReadBytes (offset1);
            var alpha_map = m_input.ReadBytes (alpha_offset - offset1);
            var alpha = m_input.ReadBytes (rgb_offset - alpha_offset);
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 4];

            int bit = 1;
            int bit_src = 0;
            int alpha_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                bool has_alpha = (bit & alpha_map[bit_src]) != 0;
                if (has_alpha || (bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                    if (has_alpha)
                    {
                        m_output[dst+3] = alpha[alpha_src];
                        alpha_src += 3;
                    }
                    else
                        m_output[dst+3] = 0xFF;
                }
                dst += 4;
                if (0x80 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
                else
                    bit <<= 1;
            }
        }

        int GetInt ()
        {
            byte a = m_input.ReadByte();
            if (a == m_scheme.Value3)
                a = 0;
            int d = 0;
            int c = 0;
            for (;;)
            {
                byte a1 = m_input.ReadByte();
                if (a1 == m_scheme.Value2)
                    break;
                if (a1 != m_scheme.Value1)
                {
                    c = (a1 == m_scheme.Value3) ? 0 : a1;
                }
                else
                {
                    ++d;
                }
            }
            return a + (c + d * m_scheme.Value1) * m_scheme.Value1;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
