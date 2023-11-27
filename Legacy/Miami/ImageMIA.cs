//! \file       ImageMIA.cs
//! \date       2023 Oct 21
//! \brief      Miamisoft image format (PC-98).
//
// Copyright (C) 2023 by morkt
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
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [950630][Miamisoft] Kotohigaoka Monogatari

namespace GameRes.Formats.Miami
{
    [Export(typeof(ImageFormat))]
    public class MiaFormat : ImageFormat
    {
        public override string         Tag => "MIA";
        public override string Description => "Miamisoft image format";
        public override uint     Signature => 0;

        public MiaFormat ()
        {
            Signatures = new[] { 0x40u, 0u };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (0xA, "CoB42"))
                return null;
            return new ImageMetaData {
                Width = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new MiaReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MiaFormat.Write not implemented");
        }
    }

    internal class MiaReader
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;

        public MiaReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        byte[] m_buffer;
        int m_buf_dst;
        byte[] m_order;

        byte[] m_output;
        int m_output_dst;
        int m_output_stride;

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            var palette = ReadPalette (m_input);
            try
            {
                UnpackInternal();
            }
            catch (EndOfStreamException)
            {
                FlushBuffer();
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, m_output, m_output_stride);
        }

        void UnpackInternal () // 1374:7A54
        {
            m_output_stride = m_info.iWidth >> 1;
            m_output = new byte[m_output_stride * m_info.iHeight];
            int buffer_size = m_info.iHeight * 0x10;
            m_buffer = new byte[buffer_size];
            SetupPattern();
            m_order = m_input.ReadBytes (6);
            byte prev_pixel = 0x10;
            m_buf_dst = 0;
            m_output_dst = 0;
            while (m_output_dst < m_output_stride)
            {
                int ctl = GetInt() - 1;
                if (ctl < 0) // @1@
                {
                    m_buffer[m_buf_dst  ] = 0;
                    m_buffer[m_buf_dst+1] = 0;
                    m_buffer[m_buf_dst+2] = 0;
                    m_buffer[m_buf_dst+3] = 0;
                    for (int i = 0; i < 4; ++i)
                    {
                        int count = GetInt();
                        int dst = count + (prev_pixel << 4);
                        byte al = m_pattern[dst];
                        int src = dst - 1;
                        while (count --> 0)
                            m_pattern[dst--] = m_pattern[src--];
                        m_pattern[dst] = al;
                        prev_pixel = al;
                        for (int j = 0; j < 4; ++j)
                        {
                            m_buffer[m_buf_dst+j] <<= 1;
                            m_buffer[m_buf_dst+j] |= (byte)(al & 1);
                            al >>= 1;
                        }
                    }
                    ushort ax = LittleEndian.ToUInt16 (m_buffer, m_buf_dst);
                    ax <<= 4;
                    LittleEndian.Pack (ax, m_buffer, m_buf_dst+4);
                    ax = LittleEndian.ToUInt16 (m_buffer, m_buf_dst+2);
                    ax <<= 4;
                    LittleEndian.Pack (ax, m_buffer, m_buf_dst+6);
                    m_buf_dst += 8;
                }
                else if (ctl < 5) // @2@
                {
                    int count = 1 + GetInt();
                    switch (m_order[ctl])
                    {
                    case 1: CopyOp01 (count, 8); break;
                    case 2: CopyOp01 (count, 0x10); break;
                    case 3: CopyOp01 (count, 0x20); break;
                    case 4: CopyOp01 (count, m_info.iHeight << 3); break;
                    case 5: CopyOp05 (count); break;
                    default: throw new InvalidFormatException();
                    }
                }
                else // ctl >= 5
                {
                    throw new InvalidFormatException();
                }
                if (buffer_size == m_buf_dst)
                    FlushBuffer();
            }
        }

        int GetInt ()
        {
            int count = 0;
            while (GetNextBit() == 0)
                ++count;
            return count;
        }

        void CopyOp01 (int count, int offset)
        {
            int bx = count;
            while (count > 0)
            {
                int dst = m_buf_dst;
                if (dst < offset)
                {
                    int src = dst;
                    dst = -(dst - offset) >> 3;
                    if (count > dst)
                        count = dst;
                    src += m_info.iHeight << 4;
                    src -= offset;
                    bx -= count;
                    count <<= 3;
                    Binary.CopyOverlapped (m_buffer, src, m_buf_dst, count);
                    m_buf_dst += count;
                    count = bx;
                    if (0 == count)
                        break;
                }
                int remaining = m_buffer.Length - m_buf_dst;
                remaining >>= 3;
                if (count > remaining)
                    count = remaining;
                bx -= count;
                count <<= 3;
                Binary.CopyOverlapped (m_buffer, m_buf_dst - offset, m_buf_dst, count);
                m_buf_dst += count;
                count = bx;
                if (m_buffer.Length == m_buf_dst)
                    FlushBuffer();
            }
        }

        void CopyOp05 (int count)
        {
            int src = m_buf_dst - 8;
            if (src < 0)
                src = m_buffer.Length - 8;

            ushort ax = LittleEndian.ToUInt16 (m_buffer, src);
            ax = (ushort)((ax << 1) & 0x0A0A | (ax >> 1) & 0x0505);
            LittleEndian.Pack (ax, m_buffer, m_buf_dst);
            ax <<= 4;
            LittleEndian.Pack (ax, m_buffer, m_buf_dst+4);

            ax = LittleEndian.ToUInt16 (m_buffer, src+2);
            ax = (ushort)((ax << 1) & 0x0A0A | (ax >> 1) & 0x0505);
            LittleEndian.Pack (ax, m_buffer, m_buf_dst+2);
            ax <<= 4;
            LittleEndian.Pack (ax, m_buffer, m_buf_dst+6);

            m_buf_dst += 8;
            if (m_buf_dst == m_buffer.Length)
                FlushBuffer();
            if (--count != 0)
                CopyOp01 (count, 0x10);
        }

        void FlushBuffer ()
        {
            int height = m_info.iHeight;
            int hi = height << 3;
            int src = 0;
            int dst = m_output_dst;
            for (int y = 0; y < height; ++y)
            {
                int b0 = m_buffer[src+4] | m_buffer[src+hi  ];
                int b1 = m_buffer[src+5] | m_buffer[src+hi+1];
                int b2 = m_buffer[src+6] | m_buffer[src+hi+2];
                int b3 = m_buffer[src+7] | m_buffer[src+hi+3];
                for (int j = 0; j < 8; j += 2)
                {
                    byte px = (byte)((((b0 << j) & 0x80) >> 3)
                                   | (((b1 << j) & 0x80) >> 2)
                                   | (((b2 << j) & 0x80) >> 1)
                                   | (((b3 << j) & 0x80)     ));
                    px |= (byte)((((b0 << j) & 0x40) >> 6)
                               | (((b1 << j) & 0x40) >> 5)
                               | (((b2 << j) & 0x40) >> 4)
                               | (((b3 << j) & 0x40) >> 3));
                    m_output[dst+(j>>1)] = px;
                }
                src += 8;
                dst += m_output_stride;
            }
            m_output_dst += 4;
            m_buf_dst = 0;
        }

        byte[] m_pattern = new byte[0x110];

        void SetupPattern ()
        {
            int dst = 0;
            byte h = 0;
            for (int i = 0; i < 0x11; ++i)
            {
                byte l = h;
                for (int j = 0; j < 0x10; ++j)
                    m_pattern[dst++] = (byte)(l++ & 0xF);
                h++;
            }
        }

        int m_bit_count = 0;
        int m_bits;

        byte GetNextBit ()
        {
            if (--m_bit_count <= 0)
            {
                m_bits = m_input.ReadUInt8();
                m_bit_count = 8;
            }
            int bit = m_bits & 1;
            m_bits >>= 1;
            return (byte)bit;
        }

        BitmapPalette ReadPalette (IBinaryStream input)
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte g = m_input.ReadUInt8();
                byte r = m_input.ReadUInt8();
                byte b = m_input.ReadUInt8();
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }
    }
}
