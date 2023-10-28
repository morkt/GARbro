//! \file       ImageGIZ2.cs
//! \date       2023 Oct 02
//! \brief      ADVIZ engine image format (PC-98).
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [951027][Ange] Leap Toki ni Sarawareta Shoujo

namespace GameRes.Formats.Adviz
{
    [Export(typeof(ImageFormat))]
    public class GizFormat : ImageFormat
    {
        public override string         Tag => "GIZ/2";
        public override string Description => "ADVIZ engine image format";
        public override uint     Signature => 0x325A4947; // 'GIZ2'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int xy = header.ToUInt16 (4);
            return new GizMetaData {
                Width  = (uint)header.ToUInt16 (6) << 3,
                Height = header.ToUInt16 (8),
                OffsetX = (xy % 0x50) << 3,
                OffsetY = xy / 0x50,
                RleCode = header[0xC],
                PlaneMap = header[0xE],
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Giz2Reader (file, (GizMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GizFormat.Write not implemented");
        }
    }

    internal class Giz2Reader
    {
        IBinaryStream   m_input;
        GizMetaData     m_info;
        BitmapPalette   m_palette;

        public BitmapPalette Palette => m_palette;

        public Giz2Reader (IBinaryStream input, GizMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        int m_stride;
        byte[][] m_planes;
        int m_output_stride;
        byte[] m_output;

        public ImageData Unpack ()
        {
            m_palette = BizFormat.ReadPalette (m_input.Name, 0x30, (pal, off) => ReadPalette (pal, off));
            if (null == m_palette)
            {
//                m_palette = BitmapPalettes.Gray16;
                throw new FileNotFoundException ("Unable to retrieve palette.");
            }
            m_input.Position = 0x10;
            m_stride = m_info.iWidth >> 3;
            int plane_size = m_info.iHeight;
            m_planes = new byte[][] {
                new byte[plane_size], new byte[plane_size], new byte[plane_size], new byte[plane_size],
            };
            m_output_stride = m_info.iWidth >> 1;
            m_output = new byte[m_output_stride * m_info.iHeight];
            int dst = 0;
            for (int x = 0; x < m_stride; ++x)
            {
                int plane_mask = 1;
                for (int i = 0; i < 4; ++i)
                {
                    if ((m_info.PlaneMap & plane_mask) == 0)
                        UnpackPlane (m_planes[i], 0);
                    plane_mask <<= 1;
                }
                CopyPlanes (dst);
                dst += 4;
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, Palette, m_output, m_output_stride);
        }

        bool UnpackPlane (byte[] output, int dst)
        {
            for (int y = 0; y < m_info.iHeight; )
            {
                byte b = m_input.ReadUInt8();
                int ctl = (b - m_info.RleCode) & 0xFF;
                if (2 == ctl)
                {
                    output[dst++] = m_input.ReadUInt8();
                }
                else if (ctl < 4)
                {
                    if (0 == ctl)
                        b = 0;
                    else if (1 == ctl)
                        b = 0xFF;
                    else
                        b = m_input.ReadUInt8();
                    int count = ((m_input.ReadUInt8() - 1) & 0xFF) + 1;
                    y += count;
                    while (count --> 0)
                    {
                        output[dst++] = b;
                    }
                    continue;
                }
                else if (ctl < 7)
                {
                    byte b0 = m_input.ReadUInt8();
                    byte b1 = m_input.ReadUInt8();
                    int count;
                    if (4 == ctl)
                    {
                        count = ((b1 - 1) & 0x7F) + 1;
                        if (b1 < 0x80)
                            b1 = Binary.RotByteL (b0, 1);
                        else
                            b1 = Binary.RotByteR (b0, 1);
                    }
                    else if (5 == ctl)
                    {
                        count = ((b1 - 1) & 0x7F) + 1;
                        if (b1 < 0x80)
                            b1 = Binary.RotByteL (b0, 2);
                        else
                            b1 = Binary.RotByteR (b0, 2);
                    }
                    else
                    {
                        count = ((m_input.ReadUInt8() - 1) & 0xFF) + 1;
                        count *= 2;
                    }
                    y += count;
                    do
                    {
                        output[dst++] = b0;
                        if (--count <= 0)
                            break;
                        output[dst++] = b1;
                    }
                    while (--count > 0);
                    continue;
                }
                else
                {
                    output[dst++] = b;
                }
                ++y;
            }
            return true;
        }

        void CopyPlanes (int dst)
        {
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int b0 = m_planes[0][y];
                int b1 = m_planes[1][y];
                int b2 = m_planes[2][y];
                int b3 = m_planes[3][y];
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
                    m_output[dst+j/2] = px;
                }
                dst += m_output_stride;
            }
        }

        BitmapPalette ReadPalette (ArcView file, int offset)
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte b = file.View.ReadByte (offset++);
                byte r = file.View.ReadByte (offset++);
                byte g = file.View.ReadByte (offset++);
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }
    }
}
