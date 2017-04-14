//! \file       DxtDecoder.cs
//! \date       Fri Apr 14 07:34:50 2017
//! \brief      DXT decompressor.
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

using GameRes.Utility;

namespace GameRes.Formats.DirectDraw
{
    public class DxtDecoder
    {
        byte[]      m_input;
        byte[]      m_output;
        int         m_width;
        int         m_height;
        int         m_output_stride;

        public DxtDecoder (byte[] input, ImageMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_output_stride = m_width * 4;
            m_height = (int)info.Height;
            m_output = new byte[m_output_stride*m_height];
        }

        public byte[] UnpackDXT5 ()
        {
            int src = 0;
            for (int y = 0; y < m_height; y += 4)
            for (int x = 0; x < m_width; x += 4)
            {
                DecompressDXT5Block (m_input, src, y, x);
                src += 16;
            }
            return m_output;
        }

        byte[] m_dxt5_alpha = new byte[16];

        void DecompressDXT5Block (byte[] input, int src, int block_y, int block_x)
        {
            byte alpha0 = input[src];
            byte alpha1 = input[src+1];

            DecompressDXT5Alpha (input, src+2, m_dxt5_alpha);

            ushort color0 = LittleEndian.ToUInt16 (input, src+8);
            ushort color1 = LittleEndian.ToUInt16 (input, src+10);

            int t = (color0 >> 11) * 255 + 16;
            byte r0 = (byte)((t / 32 + t) / 32);
            t = ((color0 & 0x07E0) >> 5) * 255 + 32;
            byte g0 = (byte)((t / 64 + t) / 64);
            t = (color0 & 0x001F) * 255 + 16;
            byte b0 = (byte)((t / 32 + t) / 32);

            t = (color1 >> 11) * 255 + 16;
            byte r1 = (byte)((t / 32 + t) / 32);
            t = ((color1 & 0x07E0) >> 5) * 255 + 32;
            byte g1 = (byte)((t / 64 + t) / 64);
            t = (color1 & 0x001F) * 255 + 16;
            byte b1 = (byte)((t / 32 + t) / 32);

            uint code = LittleEndian.ToUInt32 (input, src+12);

            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int alpha_code = m_dxt5_alpha[4 * y + x];
                byte alpha;
                if (0 == alpha_code)
                    alpha = alpha0;
                else if (1 == alpha_code)
                    alpha = alpha1;
                else if (alpha0 > alpha1)
                    alpha = (byte)(((8 - alpha_code) * alpha0 + (alpha_code - 1) * alpha1) / 7);
                else if (6 == alpha_code)
                    alpha = 0;
                else if (7 == alpha_code)
                    alpha = 0xFF;
                else
                    alpha = (byte)(((6 - alpha_code) * alpha0 + (alpha_code - 1) * alpha1) / 5);

                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;
                switch (code & 3)
                {
                case 0:
                    PutPixel (dst, r0, g0, b0, alpha);
                    break;
                case 1:
                    PutPixel (dst, r1, g1, b1, alpha);
                    break;
                case 2:
                    PutPixel (dst, (byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), alpha);
                    break;
                case 3:
                    PutPixel (dst, (byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), alpha);
                    break;
                }
                code >>= 2;
            }
        }

        static void DecompressDXT5Alpha (byte[] input, int src, byte[] output)
        {
            int dst = 0;
            for (int j = 0; j < 2; ++j)
            {
                int block = input[src++];
                block |= input[src++] << 8;
                block |= input[src++] << 16;

                for (int i = 0; i < 8; ++i)
                {
                    output[dst++] = (byte)(block & 7);
                    block >>= 3;
                }
            }
        }

        void PutPixel (int dst, byte r, byte g, byte b, byte a)
        {
            m_output[dst]   = b;
            m_output[dst+1] = g;
            m_output[dst+2] = r;
            m_output[dst+3] = a;
        }
    }
}
