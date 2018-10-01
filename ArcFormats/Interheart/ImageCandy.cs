//! \file       ImageCandy.cs
//! \date       2018 Aug 24
//! \brief      Candy Soft image format.
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
using GameRes.Utility;

namespace GameRes.Formats.Interheart
{
    internal class CandyMetaData : ImageMetaData
    {
        public int  Colors;
        public int  HeaderSize;
        public int  Version;
    }

    [Export(typeof(ImageFormat))]
    public class CandyFormat : ImageFormat
    {
        public override string         Tag { get { return "EPF"; } }
        public override string Description { get { return "Candy Soft image format"; } }
        public override uint     Signature { get { return 0; } }

        public CandyFormat ()
        {
            Signatures = new uint[] { 0x0E00, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (14);
            int header_size = BigEndian.ToUInt16 (header, 0);
            CandyMetaData info;
            if (14 == header_size)
            {
                info = new CandyMetaData {
                    OffsetX = BigEndian.ToInt16 (header, 2),
                    OffsetY = BigEndian.ToInt16 (header, 4),
                    Width   = BigEndian.ToUInt16 (header, 6),
                    Height  = BigEndian.ToUInt16 (header, 8),
                    BPP     = header[0xA],
                    Colors  = BigEndian.ToUInt16 (header, 0xC),
                    Version = header[0xB],
                    HeaderSize = header_size,
                };
            }
            else if (10 == header_size)
            {
                info = new CandyMetaData {
                    Width   = BigEndian.ToUInt16 (header, 2),
                    Height  = BigEndian.ToUInt16 (header, 4),
                    BPP     = header[6],
                    Colors  = BigEndian.ToUInt16 (header, 8),
                    Version = header[7],
                    HeaderSize = header_size,
                };
            }
            else
                return null;
            if (info.Version != 1 && info.Version != 2 || info.BPP < 8 || info.BPP > 32)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CandyDecoder (file, (CandyMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CandyFormat.Write not implemented");
        }
    }

    internal class CandyDecoder
    {
        IBinaryStream   m_input;
        CandyMetaData   m_info;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_colors;
        int             m_pixel_size;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public CandyDecoder (IBinaryStream input, CandyMetaData info)
        {
            m_input = input;
            m_info = info;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_colors = info.Colors;
            if (24 == info.BPP || (32 == info.BPP && 1 == info.Version))
            {
                m_pixel_size = 3;
                m_stride = m_width * 3;
                Format = PixelFormats.Bgr24;
            }
            else if (32 == info.BPP)
            {
                m_pixel_size = 4;
                m_stride = m_width * 4;
                Format = PixelFormats.Bgra32;
            }
            else
            {
                m_pixel_size = 1;
                m_stride = m_width;
                Format = PixelFormats.Indexed8;
            }
            m_output = new byte[m_stride * m_height];
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.HeaderSize;
            if (m_colors > 0)
                Palette = ReadPalette();
            LzUnpack();
            return RestorePixels();
        }

        byte[] RestorePixels ()
        {
            var pixels = new byte[m_output.Length];
            int src = 0;
            int block_size = m_info.BPP == 8 ? 144 : 80;
            for (int block_y = 0; block_y < m_height; block_y += block_size)
            for (int block_x = 0; block_x < m_width; block_x += block_size)
            {
                int dst_col = block_y * m_stride + block_x * m_pixel_size;
                int block_height = Math.Min (block_size, m_height - block_y);
                int block_width  = Math.Min (block_size, m_width - block_x);
                for (int x = 0; x < block_width; ++x)
                {
                    int dst = dst_col;
                    for (int y = 0; y < block_height; ++y)
                    {
                        if (m_pixel_size > 3)
                        {
                            pixels[dst+3] = m_output[src ];
                            pixels[dst+2] = m_output[src+1];
                            pixels[dst+1] = m_output[src+2];
                            pixels[dst  ] = m_output[src+3];
                        }
                        else if (1 == m_pixel_size)
                        {
                            pixels[dst] = m_output[src];
                        }
                        else
                        {
                            pixels[dst+2] = m_output[src ];
                            pixels[dst+1] = m_output[src+1];
                            pixels[dst  ] = m_output[src+2];
                        }
                        dst += m_stride;
                        src += m_pixel_size;
                    }
                    dst_col += m_pixel_size;
                }
            }
            return pixels;
        }

        void LzUnpack ()
        {
            var bits = new byte[16];
            var frame = new byte[0x1000];
            int frame_pos = 0;
            int dst = 0;
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                bits[0] = (byte)(ctl & 1);
                bits[1] = 1;
                int count = 0;
                for (int i = 1; i < 8; ++i)
                {
                    ctl >>= 1;
                    if (bits[2*count] == (ctl & 1))
                    {
                        ++bits[2*count + 1];
                    }
                    else
                    {
                        ++count;
                        bits[2*count] = (byte)(ctl & 1);
                        bits[2*count + 1] = 1;
                    }
                }
                for (int i = 0; i <= count && dst < m_output.Length; ++i)
                {
                    int bpos = 2*i;
                    if (bits[bpos++] != 0)
                    {
                        while (bits[bpos] > 0)
                        {
                            byte b = m_input.ReadUInt8();
                            m_output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                            --bits[bpos];
                        }
                    }
                    else
                    {
                        while (bits[bpos] > 0 && dst < m_output.Length)
                        {
                            int offset = m_input.ReadUInt16();
                            int zcount = Math.Min ((offset & 0xF) + 3, m_output.Length - dst);
                            offset >>= 4;
                            for (int j = 0; j < zcount; ++j)
                            {
                                byte b = frame[offset++ & 0xFFF];
                                m_output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                            }
                            --bits[bpos];
                        }
                    }
                }
            }
        }

        BitmapPalette ReadPalette ()
        {
            var palette_data = new byte[4 * m_colors];
            if (palette_data.Length != m_input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            int src = 0;
            var color_map = new Color[m_colors];
            for (int i = 0; i < m_colors; ++i)
            {
                color_map[i] = Color.FromRgb (palette_data[src+1], palette_data[src+2], palette_data[src+3]);
                src += 4;
            }
            return new BitmapPalette (color_map);
        }
    }
}
