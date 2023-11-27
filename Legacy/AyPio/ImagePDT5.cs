//! \file       ImagePDT5.cs
//! \date       2023 Oct 16
//! \brief      AyPio image format.
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

// [960726][AyPio] Chuushaki

namespace GameRes.Formats.AyPio
{
    [Export(typeof(ImageFormat))]
    public class Pdt5Format : ImageFormat
    {
        public override string         Tag => "PDT/5";
        public override string Description => "UK2 engine image format";
        public override uint     Signature => 0;

        public Pdt5Format ()
        {
            Extensions = new[] { "pdt", "anm" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.ReadByte() != 0x35)
                return null;
            file.Position = 0x21;
            int left   = file.ReadUInt16();
            int top    = file.ReadUInt16();
            int right  = file.ReadUInt16();
            int bottom = file.ReadUInt16();
            int width = (right - left + 1) << 3;
            int height = bottom - top + 1;
            if (width <= 0 || height <= 0 || width > 640 || height > 1024)
                return null;
            return new ImageMetaData {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = left << 3,
                OffsetY = top,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Pdt5Reader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pdt5Format.Write not implemented");
        }
    }

    internal class Pdt5Reader
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;

        public Pdt5Reader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        byte[] m_buffer;

        public ImageData Unpack ()
        {
            m_input.Position = 1;
            var palette = ReadPalette();
            m_input.Position = 0x29;
            int width = m_info.iWidth;
            int height = m_info.iHeight;
            int output_stride = m_info.iWidth;
            var pixels = new byte[output_stride * height];
            InitFrame();
            InitBitReader();
            byte px = 0;
            m_buffer = new byte[1932];
            int output_pos = 0;
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    if (GetNextBit() != 0)
                    {
                        if (GetNextBit() != 0)
                        {
                            if (GetNextBit() != 0)
                            {
                                int count = GetCount() + 2;
                                int pos = 1290 + x;
                                x += count - 1;
                                while (count --> 0)
                                {
                                    m_buffer[pos++] = px;
                                }
                            }
                            else
                            {
                                int count = GetCount() + 1;
                                px = m_buffer[1289 + x];
                                int src = x + 1288;
                                int dst = x + 1290;
                                Binary.CopyOverlapped (m_buffer, src, dst, count * 2);
                                x += count * 2 - 1;
                            }
                        }
                        else
                        {
                            px = GetPixel (x);
                            m_buffer[x + 1290] = px;
                        }
                    }
                    else
                    {
                        int count = 0;
                        byte b = GetPixel (x);
                        while (GetNextBit() != 1)
                            ++count;
                        int src = 0x10 * b + count;
                        px = m_frame[src];
                        m_buffer[x + 1290] = px;
                        while (count --> 0)
                        {
                            m_frame[src] = m_frame[src-1];
                            --src;
                        }
                        m_frame[src] = px;
                    }
                }
                Buffer.BlockCopy (m_buffer, 1290, pixels, output_pos, width);
                output_pos += output_stride;
                Buffer.BlockCopy (m_buffer, 644, m_buffer, 0, 1288);
            }
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, pixels, output_stride);
        }

        byte GetPixel (int src)
        {
            byte px = m_buffer[src + 647];
            if (m_buffer[src + 4] != px)
            {
                byte v = m_buffer[src + 2];
                if (v != px)
                {
                    px = m_buffer[src + 645];
                    if (px != v && m_buffer[src] != px)
                        return m_buffer[src + 2];
                }
            }
            return px;
        }

        byte[] m_frame;

        void InitFrame ()
        {
            m_frame = new byte[0x110];
            for (int j = 0; j < 0x110; j += 0x10)
            {
                for (byte i = 0; i < 0x10; ++i)
                    m_frame[j + i] = i;
            }
        }

        int GetCount ()
        {
            int count = 0;
            int bits = 1;
            while (GetNextBit() != 1)
            {
                count += bits;
                bits <<= 1;
            }
            if (bits > 1)
            {
                do
                {
                    if (GetNextBit() != 0)
                        count += bits;
                    bits >>= 1;
                }
                while (bits != 0);
            }
            return count;
        }

        uint m_bits;
        int m_bit_count;

        void InitBitReader ()
        {
            m_bit_count = 1;
        }

        byte GetNextBit ()
        {
            if (--m_bit_count <= 0)
            {
                m_bits = m_input.ReadUInt8();
                m_bit_count = 8;
            }
            uint bit = m_bits & 1;
            m_bits >>= 1;
            return (byte)bit;
        }

        BitmapPalette ReadPalette ()
        {
            var colors = new Color[16];
            for (int i = 0; i < 16; ++i)
            {
                ushort rgb = m_input.ReadUInt16();
                int b = (rgb & 0xF) * 0x11;
                int r = ((rgb >> 4) & 0xF) * 0x11;
                int g = ((rgb >> 8) & 0xF) * 0x11;
                colors[i] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
            }
            return new BitmapPalette (colors);
        }
    }
}
