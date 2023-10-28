//! \file       ImageTSZ.cs
//! \date       2023 Sep 27
//! \brief      Ponytail NMI 2.05 image format (PC-98).
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [930413][Ponytail Soft] Yougen Doumu

namespace GameRes.Formats.Ponytail
{
    [Export(typeof(ImageFormat))]
    public class TszFormat : ImageFormat
    {
        public override string         Tag => "TSZ";
        public override string Description => "Ponytail Soft NMI image format";
        public override uint     Signature => 0x20494D4E; // 'NMI '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (4, "2.05"))
                return null;
            return new ImageMetaData {
                Width  = (uint)header.ToUInt16 (0xC) << 2,
                Height = header.ToUInt16 (0xE),
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new TszReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TszFormat.Write not implemented");
        }
    }

    internal class TszReader
    {
        protected IBinaryStream   m_input;
        protected ImageMetaData   m_info;
        protected int             m_stride;

        protected TszReader () { }

        public TszReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = m_info.iWidth >> 1;
        }

        private   int       m_previous_row;
        private   ushort[]  m_linebuffer;
        private   int       m_dst;

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            var palette = ReadPalette();
            var output = new byte[m_stride * m_info.iHeight];
            m_linebuffer = new ushort[m_info.iHeight * 2];
            m_previous_row = m_info.iHeight;
            int width = m_info.iWidth >> 2;
            int dst_x = 0;
            int x = 0;
            ResetBitReader();
            while (x < width)
            {
                int y = 0;
                m_dst = 0;
                if ((x & 1) != 0)
                    m_dst += m_info.iHeight;
                while (y < m_info.iHeight)
                {
                    int ctl = 0;
                    while (GetNextBit())
                    {
                        ++ctl;
                    }
                    int count; // bx
                    switch (ctl)
                    {
                    case 0: count = CopyMethod0(); break;
                    case 1: count = CopyMethod1(); break;
                    case 2:
                        m_linebuffer[m_dst] = m_input.ReadUInt16();
                        count = 1;
                        break;
                    case 3: count = CopyMethod3(); break;
                    case 4: count = CopyMethod4(); break;
                    default: throw new InvalidFormatException();
                    }
                    m_dst += count;
                    y += count;
                }
                if ((x & 1) != 0)
                {
                    CopyScanline (output, dst_x);
                    dst_x += 4;
                }
                m_previous_row = -m_previous_row;
                ++x;
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, output, m_stride);
        }

        void CopyScanline (byte[] output, int dst)
        {
            for (int i = 0; i < m_info.iHeight; ++i)
            {
                ushort px1 = m_linebuffer[i];
                ushort px2 = m_linebuffer[i + m_info.iHeight];
                // these bytes contain 8 pixels that are being put into 4 planes
                int b0 = (px1 << 4) & 0xF0 | (px2      ) & 0x0F;
                int b1 = (px1     ) & 0xF0 | (px2 >>  4) & 0x0F;
                int b2 = (px1 >> 4) & 0xF0 | (px2 >>  8) & 0x0F;
                int b3 = (px1 >> 8) & 0xF0 | (px2 >> 12) & 0x0F;
                // repack pixels into flat surface, 2 pixels per byte
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
                    output[dst+j/2] = px;
                }
                dst += m_stride;
            }
        }

        int CopyMethod0 ()
        {
            int count = GetBitLength();
            int offset = GetBits (4);
            offset += m_previous_row - 8;
            CopyOverlapped (m_linebuffer, m_dst + offset, m_dst, count);
            return count;
        }

        int CopyMethod1 ()
        {
            int count = GetBitLength();
            int offset = m_input.ReadUInt8();
            offset += m_previous_row - 0x80;
            CopyOverlapped (m_linebuffer, m_dst + offset, m_dst, count);
            return count;
        }

        int CopyMethod3 ()
        {
            int count = GetBitLength();
            int offset = GetBits (4);
            offset -= 0x10;
            CopyOverlapped (m_linebuffer, m_dst + offset, m_dst, count);
            return count;
        }

        int CopyMethod4 ()
        {
            byte al = m_input.ReadUInt8();
            int nibble = al >> 4;
            ushort mask1 = s_pattern1[al >> 4];
            ushort mask2 = s_pattern2[al & 0xF];
            ushort pixel = m_linebuffer[m_dst + m_previous_row];
            pixel &= mask1;
            for (int i = 0; i < 4; ++i)
            {
                short carry = (short)(nibble & 1);
                nibble >>= 1;
                pixel |= (ushort)(-carry & mask2);
                pixel = RotU16R (pixel, 1);
            }
            pixel = RotU16L (pixel, 4);
            m_linebuffer[m_dst] = pixel;
            return 1;
        }

        static readonly ushort[] s_pattern1 = new ushort[] {
            0xFFFF, 0xEEEE, 0xDDDD, 0xCCCC, 0xBBBB, 0xAAAA, 0x9999, 0x8888,
            0x7777, 0x6666, 0x5555, 0x4444, 0x3333, 0x2222, 0x1111, 0x0000,
        };
        static readonly ushort[] s_pattern2 = new ushort[] {
            0x0000, 0x0001, 0x0010, 0x0011, 0x0100, 0x0101, 0x0110, 0x0111,
            0x1000, 0x1001, 0x1010, 0x1011, 0x1100, 0x1101, 0x1110, 0x1111,
        };

        ushort m_bits;
        int m_bit_count;

        protected void ResetBitReader ()
        {
            m_bits = 0;
            m_bit_count = 0;
        }

        protected int GetBitLength ()
        {
            if (!GetNextBit())
                return 1;
            int count = 1;
            while (GetNextBit())
            {
                ++count;
            }
            return GetBits (count) | 1 << count;
        }

        protected bool GetNextBit ()
        {
            if (--m_bit_count < 0)
            {
                m_bits = m_input.ReadUInt16();
                m_bit_count = 15;
            }
            bool bit = (m_bits & 0x8000) != 0;
            m_bits <<= 1;
            return bit;
        }

        static readonly ushort[] s_bit_mask = new ushort[] {
            0, 1, 3, 7, 0xF, 0x1F, 0x3F, 0x7F, 0xFF, 0x1FF, 0x3FF, 0x7FF, 0xFFF, 0x1FFF, 0x3FFF, 0x7FFF, 0xFFFF,
        };

        protected int GetBits (int count)
        {
            m_bit_count -= count;
            if (m_bit_count < 0)
            {
                m_bits = RotU16L (m_bits, count);
                int cl = -m_bit_count;
                ushort bits = m_input.ReadUInt16();
                bits = RotU16L (bits, cl);
                ushort mask = s_bit_mask[cl];
                ushort new_bits = (ushort)(bits & ~mask);
                bits &= mask;
                bits |= m_bits;
                m_bits = new_bits;
                m_bit_count = 16 - cl;
                return bits;
            }
            else
            {
                m_bits = RotU16L (m_bits, count);
                ushort mask = s_bit_mask[count];
                int bits = m_bits & mask;
                m_bits &= (ushort)~mask;
                return bits;
            }
        }

        protected BitmapPalette ReadPalette ()
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte r = m_input.ReadUInt8();
                byte g = m_input.ReadUInt8();
                byte b = m_input.ReadUInt8();
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }

        static internal ushort RotU16L (ushort val, int count)
        {
            return (ushort)(val << count | val >> (16 - count));
        }

        static internal ushort RotU16R (ushort val, int count)
        {
            return (ushort)(val >> count | val << (16 - count));
        }

        static internal void CopyOverlapped (ushort[] data, int src, int dst, int count)
        {
            src <<= 1;
            dst <<= 1;
            count <<= 1;
            if (dst > src)
            {
                while (count > 0)
                {
                    int preceding = Math.Min (dst - src, count);
                    Buffer.BlockCopy (data, src, data, dst, preceding);
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                Buffer.BlockCopy (data, src, data, dst, count);
            }
        }
    }
}
