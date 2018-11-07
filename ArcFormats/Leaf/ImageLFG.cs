//! \file       ImageLFG.cs
//! \date       2018 Nov 06
//! \brief      Leaf 16-color image format.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.Leaf
{
    internal class LfgMetaData : ImageMetaData
    {
        public byte Mode;
        public byte KeyColor;
        public int  ImageSize;
    }

    [Export(typeof(ImageFormat))]
    public class LfgFormat : ImageFormat
    {
        public override string         Tag { get { return "LFG"; } }
        public override string Description { get { return "Leaf image format"; } }
        public override uint     Signature { get { return 0x4641454C; } } // 'LEAFCODE'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            if (!header.AsciiEqual ("LEAFCODE"))
                return null;
            int x = header.ToInt16 (0x20);
            int y = header.ToInt16 (0x22);
            return new LfgMetaData {
                Width  = (uint)(header.ToInt16 (0x24) - x + 1) * 8,
                Height = (uint)(header.ToInt16 (0x26) - y + 1),
                OffsetX = x,
                OffsetY = y,
                BPP     = 4,
                Mode    = header[0x28],
                KeyColor = header[0x29],
                ImageSize = header.ToInt32 (0x2C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new LfgReader (file, (LfgMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LfgFormat.Write not implemented");
        }
    }

    internal class LfgReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_mode;
        int             m_length;
        int             m_stride;
        int             m_height;
        int             m_key_color;

        public PixelFormat    Format { get { return PixelFormats.Indexed4; } }
        public int            Stride { get { return m_stride; } }
        public BitmapPalette Palette { get; private set; }

        public LfgReader (IBinaryStream input, LfgMetaData info)
        {
            m_input = input;
            m_stride = (int)info.Width / 2;
            m_height = (int)info.Height;
            m_output = new byte[m_stride * m_height];
            m_mode = info.Mode;
            m_length = info.ImageSize;
            m_key_color = info.KeyColor;
        }

        byte[] m_frame = new byte[0x1000];

        public byte[] Unpack ()
        {
            m_input.Position = 8;
            Palette = ReadPalette();
            m_input.Position = 0x30;

            int frame_pos = 0xFEE;
            int dst = 0;
            int x = 0, y = 0;

            Action next_pixel;
            if (1 == m_mode)
            {
                next_pixel = () => {
                    ++dst;
                    if (++x >= m_stride)
                    {
                        x = 0;
                        dst = ++y * m_stride;
                    }
                };
            }
            else
            {
                next_pixel = () => {
                    dst += m_stride;
                    if (++y >= m_height)
                    {
                        y = 0;
                        dst = ++x;
                    }
                };
            }
            int pixel_count = 0;
            int ctl = 0;
            byte mask = 0;
            while (pixel_count < m_length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    ctl = m_input.ReadUInt8();
                    mask = 0x80;
                }
                if ((ctl & mask) != 0)
                {
                    byte color = ColorMap[m_input.ReadUInt8()];
                    m_frame[frame_pos++ & 0xFFF] = color;
                    m_output[dst] = color;
                    next_pixel();
                    ++pixel_count;
                }
                else
                {
                    int offset = m_input.ReadUInt16();
                    int count = (offset & 0xF) + 3;
                    offset >>= 4;
                    while (count --> 0 && pixel_count < m_length)
                    {
                        byte color = m_frame[offset++ & 0xFFF];
                        m_frame[frame_pos++ & 0xFFF] = color;
                        m_output[dst] = color;
                        next_pixel();
                        ++pixel_count;
                    }
                }
            }
            return m_output;
        }

        BitmapPalette ReadPalette ()
        {
            var color_data = new byte[0x18 * 2];
            for (int i = 0; i < color_data.Length; i += 2)
            {
                byte c = m_input.ReadUInt8();
                color_data[i  ] = (byte)((c >> 4) * 0x11);
                color_data[i+1] = (byte)((c & 0xF) * 0x11);
            }
            var colors = new Color[16];
            int src = 0;
            for (int i = 0; i < 16; ++i)
            {
                if (m_key_color == i)
                    colors[i] = Color.FromArgb (0, color_data[src], color_data[src+1], color_data[src+2]);
                else
                    colors[i] = Color.FromRgb (color_data[src], color_data[src+1], color_data[src+2]);
                src += 3;
            }
//            colors[15] = Color.FromRgb (0xFF, 0xFF, 0xFF);
            return new BitmapPalette (colors);
        }

        static readonly byte[] ColorMap = {
            0x00, 0x01, 0x10, 0x11, 0x02, 0x03, 0x12, 0x13, 0x20, 0x21, 0x30, 0x31, 0x22, 0x23, 0x32, 0x33,
            0x04, 0x05, 0x14, 0x15, 0x06, 0x07, 0x16, 0x17, 0x24, 0x25, 0x34, 0x35, 0x26, 0x27, 0x36, 0x37,
            0x40, 0x41, 0x50, 0x51, 0x42, 0x43, 0x52, 0x53, 0x60, 0x61, 0x70, 0x71, 0x62, 0x63, 0x72, 0x73,
            0x44, 0x45, 0x54, 0x55, 0x46, 0x47, 0x56, 0x57, 0x64, 0x65, 0x74, 0x75, 0x66, 0x67, 0x76, 0x77,
            0x08, 0x09, 0x18, 0x19, 0x0A, 0x0B, 0x1A, 0x1B, 0x28, 0x29, 0x38, 0x39, 0x2A, 0x2B, 0x3A, 0x3B,
            0x0C, 0x0D, 0x1C, 0x1D, 0x0E, 0x0F, 0x1E, 0x1F, 0x2C, 0x2D, 0x3C, 0x3D, 0x2E, 0x2F, 0x3E, 0x3F,
            0x48, 0x49, 0x58, 0x59, 0x4A, 0x4B, 0x5A, 0x5B, 0x68, 0x69, 0x78, 0x79, 0x6A, 0x6B, 0x7A, 0x7B,
            0x4C, 0x4D, 0x5C, 0x5D, 0x4E, 0x4F, 0x5E, 0x5F, 0x6C, 0x6D, 0x7C, 0x7D, 0x6E, 0x6F, 0x7E, 0x7F,
            0x80, 0x81, 0x90, 0x91, 0x82, 0x83, 0x92, 0x93, 0xA0, 0xA1, 0xB0, 0xB1, 0xA2, 0xA3, 0xB2, 0xB3,
            0x84, 0x85, 0x94, 0x95, 0x86, 0x87, 0x96, 0x97, 0xA4, 0xA5, 0xB4, 0xB5, 0xA6, 0xA7, 0xB6, 0xB7,
            0xC0, 0xC1, 0xD0, 0xD1, 0xC2, 0xC3, 0xD2, 0xD3, 0xE0, 0xE1, 0xF0, 0xF1, 0xE2, 0xE3, 0xF2, 0xF3,
            0xC4, 0xC5, 0xD4, 0xD5, 0xC6, 0xC7, 0xD6, 0xD7, 0xE4, 0xE5, 0xF4, 0xF5, 0xE6, 0xE7, 0xF6, 0xF7,
            0x88, 0x89, 0x98, 0x99, 0x8A, 0x8B, 0x9A, 0x9B, 0xA8, 0xA9, 0xB8, 0xB9, 0xAA, 0xAB, 0xBA, 0xBB,
            0x8C, 0x8D, 0x9C, 0x9D, 0x8E, 0x8F, 0x9E, 0x9F, 0xAC, 0xAD, 0xBC, 0xBD, 0xAE, 0xAF, 0xBE, 0xBF,
            0xC8, 0xC9, 0xD8, 0xD9, 0xCA, 0xCB, 0xDA, 0xDB, 0xE8, 0xE9, 0xF8, 0xF9, 0xEA, 0xEB, 0xFA, 0xFB,
            0xCC, 0xCD, 0xDC, 0xDD, 0xCE, 0xCF, 0xDE, 0xDF, 0xEC, 0xED, 0xFC, 0xFD, 0xEE, 0xEF, 0xFE, 0xFF
        };
    }
}
