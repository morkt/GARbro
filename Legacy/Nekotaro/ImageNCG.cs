//! \file       ImageNCG.cs
//! \date       2023 Oct 10
//! \brief      Nekotaro Game System image format (PC-98).
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Nekotaro
{
    [Export(typeof(ImageFormat))]
    public class NcgFormat : ImageFormat
    {
        public override string         Tag => "NCG";
        public override string Description => "Nekotaro Game System image format";
        public override uint     Signature => 0;

        public NcgFormat ()
        {
            Signatures = new[] { 0xC8500000u, 0u };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (4);
            int left = header[0] << 3;
            int top  = header[1] << 1;
            int width = header[2] << 3;
            int height = header[3] << 1;
            int right = left + width;
            int bottom = top + height;
            if (right > 640 || bottom > 400 || 0 == width || 0 == height)
                return null;
            return new ImageMetaData {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = left,
                OffsetY = top,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new NcgReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NcgFormat.Write not implemented");
        }
    }

    internal class NcgReader
    {
        IBinaryStream m_input;
        ImageMetaData m_info;

        public NcgReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 4;
            var palette = ReadPalette();
            int width = m_info.iWidth;
            int height = m_info.iHeight;
            int output_stride = width;
            var pixels = new byte[output_stride * height];
            int quart_width = width / 4;
            int half_height = height / 2;
            var blockmap = new bool[quart_width * half_height];
            var bits1 = new byte[8];
            var bits2 = new byte[8];
            byte ctl;
            int dst, pblk;
            do
            {
                for (int i = 0; i < 8; ++i)
                    bits1[i] = bits2[i] = 0;
                for (int shift = 0; shift < 4; ++shift)
                {
                    byte bit = (byte)(1 << shift);
                    FillBits (bits1, bit);
                    FillBits (bits2, bit);
                }
                for (;;)
                {
                    ctl = m_input.ReadUInt8();
                    if (0xFF == ctl || 0x7F == ctl)
                        break;
                    int pos = (ctl & 0x3F) << 8 | m_input.ReadUInt8();
                    int x = (pos % 80) << 3;
                    int y = (pos / 80) << 1;
                    dst = width * y + x;
                    pblk = x / 4 + quart_width * (y / 2);
                    switch (ctl >> 6)
                    {
                    case 0:
                        {
                            int w_count = m_input.ReadUInt8();
                            int h_count = m_input.ReadUInt8();
                            int gap = quart_width - 2 * w_count;
                            while (h_count --> 0)
                            {
                                for (int i = 0; i < w_count; ++i)
                                {
                                    for (int j = 0; j < 8; ++j)
                                    {
                                        pixels[dst+width] = bits2[j];
                                        pixels[dst++] = bits1[j];
                                    }
                                    blockmap[pblk++] = true;
                                    blockmap[pblk++] = true;
                                }
                                pblk += gap;
                                dst += 2 * width - 8 * w_count;
                            }
                            break;
                        }
                    case 1:
                        {
                            int count = m_input.ReadUInt8();
                            while (count --> 0)
                            {
                                for (int j = 0; j < 8; ++j)
                                {
                                    pixels[dst+width] = bits2[j];
                                    pixels[dst++] = bits1[j];
                                }
                                blockmap[pblk++] = true;
                                blockmap[pblk++] = true;
                            }
                            break;
                        }
                    case 2:
                        {
                            int count = m_input.ReadUInt8();
                            while (count --> 0)
                            {
                                for (int j = 0; j < 8; ++j)
                                {
                                    pixels[dst+width] = bits2[j];
                                    pixels[dst++] = bits1[j];
                                }
                                blockmap[pblk  ] = true;
                                blockmap[pblk+1] = true;
                                dst += 2 * width - 8;
                                pblk += quart_width;
                            }
                            break;
                        }
                    case 3:
                        {
                            for (int j = 0; j < 8; ++j)
                            {
                                pixels[dst+width] = bits2[j];
                                pixels[dst++] = bits1[j];
                            }
                            blockmap[pblk  ] = true;
                            blockmap[pblk+1] = true;
                            break;
                        }
                    }
                }
            }
            while (ctl != 0xFF);
            do
            {
                for (int i = 0; i < 8; ++i)
                    bits1[i] = 0;
                for (int shift = 0; shift < 4; ++shift)
                    FillBits (bits1, (byte)(1 << shift));
                for (;;)
                {
                    ctl = m_input.ReadUInt8();
                    if (0xFF == ctl || 0xFE == ctl)
                        break;
                    int pos = (ctl & 0x7F) << 8 | m_input.ReadUInt8();
                    dst = 4 * (pos % 160) + width * 2 * (pos / 160);
                    pblk = (pos % 160) + quart_width * (pos / 160);
                    if ((ctl & 0x80) == 0)
                    {
                        int count = m_input.ReadUInt8();
                        while (count --> 0)
                        {
                            for (int j = 0; j < 4; ++j)
                            {
                                pixels[dst+width] = bits1[j+4];
                                pixels[dst++] = bits1[j];
                            }
                            blockmap[pblk] = true;
                            pblk += quart_width;
                            dst += 2 * width - 4;
                        }
                    }
                    else
                    {
                        for (int j = 0; j < 4; ++j)
                        {
                            pixels[dst+width] = bits1[j+4];
                            pixels[dst++] = bits1[j];
                        }
                        blockmap[pblk] = true;
                    }
                }
            }
            while (ctl != 0xFF);
            dst = 0;
            pblk = 0;
            for (int y = 0; y < half_height; ++y)
            {
                for (int x = 0; x < quart_width; ++x)
                {
                    if (blockmap[pblk++])
                    {
                        dst += 4;
                    }
                    else
                    {
                        for (int i = 0; i < 8; ++i)
                            bits1[i] = 0;
                        for (int shift = 0; shift < 4; ++shift)
                            FillBits (bits1, (byte)(1 << shift));
                        for (int j = 0; j < 4; ++j)
                        {
                            pixels[dst+width] = bits1[j+4];
                            pixels[dst++] = bits1[j];
                        }
                    }
                }
                dst += width;
            }
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, pixels, output_stride);
        }

        void FillBits (byte[] bits, byte bit)
        {
            sbyte s = m_input.ReadInt8();
            for (int i = 0; i < 8; ++i)
            {
                if (s < 0)
                    bits[i] |= bit;
                s <<= 1;
            }
        }

        static readonly string PaletteKey = "NEKOTARO";

        BitmapPalette ReadPalette ()
        {
            int k = 0;
            var colors = new Color[16];
            for (int c = 0; c < 16; ++c)
            {
                int g = m_input.ReadUInt8();
                int r = m_input.ReadUInt8();
                int b = m_input.ReadUInt8();
                b = (~b - PaletteKey[k++ & 7]) & 0xFF;
                r = (~r - PaletteKey[k++ & 7]) & 0xFF;
                g = (~g - PaletteKey[k++ & 7]) & 0xFF;
                colors[c] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }
    }
}
