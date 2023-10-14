//! \file       ImagePDT.cs
//! \date       2023 Oct 13
//! \brief      AyPio image format (PC-98).
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

// [960726][AyPio] Chuushaki
// [970314][AyPio] Stars

namespace GameRes.Formats.AyPio
{
    internal class PdtMetaData : ImageMetaData
    {
        public byte Rle1;
        public byte Rle2;
    }

    [Export(typeof(ImageFormat))]
    public class PdtFormat : ImageFormat
    {
        public override string         Tag => "PDT/UK2";
        public override string Description => "UK2 engine image format";
        public override uint     Signature => 0;

        public PdtFormat ()
        {
            Extensions = new[] { "pdt", "anm" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.ReadByte() != 0x34)
                return null;
            file.Position = 0x21;
            byte rle1  = file.ReadUInt8();
            byte rle2  = file.ReadUInt8();
            int left   = file.ReadUInt16();
            int top    = file.ReadUInt16();
            int right  = file.ReadUInt16();
            int bottom = file.ReadUInt16();
            int width = (right - left + 1) << 3;
            int height = (((bottom - top) >> 1) + 1) << 1;
            if (width <= 0 || height <= 0 || width > 2048 || height > 512)
                return null;
            return new PdtMetaData {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = left << 3,
                OffsetY = top,
                BPP = 4,
                Rle1 = rle1,
                Rle2 = rle2,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PdtReader (file, (PdtMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PdtFormat.Write not implemented");
        }
    }

    internal class PdtReader
    {
        IBinaryStream   m_input;
        PdtMetaData     m_info;

        public PdtReader (IBinaryStream input, PdtMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        int m_stride;
        int m_rows;

        public ImageData Unpack ()
        {
            m_input.Position = 1;
            var palette = ReadPalette();
            m_input.Position = 0x2B;
            int output_stride = m_info.iWidth >> 1;
            int height = m_info.iHeight;
            m_stride = m_info.iWidth >> 3;
            m_rows = height >> 1;
            int plane_size = m_stride * height;
            var planes = new byte[][] {
                new byte[plane_size], new byte[plane_size], new byte[plane_size], new byte[plane_size]
            };
            for (int i = 0; i < 4; ++i)
                UnpackPlane (planes[i]);
            var pixels = new byte[output_stride * height];
            FlattenPlanes (planes, pixels);
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, pixels, output_stride);
        }

        void UnpackPlane (byte[] output)
        {
            for (int x = 0; x < m_stride; ++x)
            {
                int dst = x;
                int y = 0;
                while (y < m_rows)
                {
                    int count = 1;
                    byte p0, p1;
                    byte ctl = m_input.ReadUInt8();
                    if (ctl == m_info.Rle1)
                    {
                        count = m_input.ReadUInt8();
                        p0 = m_input.ReadUInt8();
                        p1 = m_input.ReadUInt8();
                    }
                    else if (ctl == m_info.Rle2)
                    {
                        count = m_input.ReadUInt8();
                        p1 = p0 = m_input.ReadUInt8();
                    }
                    else
                    {
                        p0 = ctl;
                        p1 = m_input.ReadUInt8();
                    }
                    while (count --> 0)
                    {
                        output[dst] = p0;
                        dst += m_stride;
                        output[dst] = p1;
                        dst += m_stride;
                        ++y;
                    }
                }
            }
        }

        void FlattenPlanes (byte[][] planes, byte[] output)
        {
            int plane_size = planes[0].Length;
            int dst = 0;
            for (int src = 0; src < plane_size; ++src)
            {
                int b0 = planes[0][src];
                int b1 = planes[1][src];
                int b2 = planes[2][src];
                int b3 = planes[3][src];
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
                    output[dst++] = px;
                }
            }
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
