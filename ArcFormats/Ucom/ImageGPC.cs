//! \file       ImageGPC.cs
//! \date       2017 Nov 22
//! \brief      For/Ucom image format.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Ucom
{
    internal class GpcMetaData : ImageMetaData
    {
        public int  PaletteColors;
    }

    [Export(typeof(ImageFormat))]
    public class GpcFormat : ImageFormat
    {
        public override string         Tag { get { return "GPC"; } }
        public override string Description { get { return "For/Ucom image format"; } }
        public override uint     Signature { get { return 0x00285047; } } // 'GP('

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x26);
            int header_length = header.ToInt32 (2);
            uint width = header.ToUInt32 (6);
            uint height = header.ToUInt32 (0xA);
            int bpp = header.ToUInt16 (0x10);
            if (bpp != 8 && bpp != 24 && bpp != 32)
                return null;
            int colors = 0;
            if (8 == bpp)
            {
                colors = header.ToInt32 (0x22);
                if (0 == colors)
                    colors = 0x100;
            }
            return new GpcMetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                PaletteColors = colors,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var gpc = new GpcReader (file, (GpcMetaData)info);
            gpc.Unpack();
            return ImageData.Create (info, gpc.Format, gpc.Palette, gpc.Data, gpc.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpcFormat.Write not implemented");
        }
    }

    internal sealed class GpcReader
    {
        IBinaryStream   m_input;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_colors;
        int             m_pixel_size;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get { return m_stride; } }

        public GpcReader (IBinaryStream input, GpcMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_pixel_size = info.BPP / 8;
            m_colors = info.PaletteColors;
            m_stride = (m_width * m_pixel_size + 3) & ~3;
            m_output = new byte[m_height * m_stride];
            if (1 == m_pixel_size)
                Format = PixelFormats.Indexed8;
            else if (3 == m_pixel_size)
                Format = PixelFormats.Bgr24;
            else
                Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            m_input.Position = 0x2A;
            if (1 == m_pixel_size)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, m_colors);
            int gap = m_stride - m_width * m_pixel_size;
            for (int dst_row = m_output.Length - m_stride; dst_row >= 0; dst_row -= m_stride)
            {
                int dst = dst_row;
                int x = 0;
                while (x < m_width)
                {
                    byte ctl = m_input.ReadUInt8();
                    int count = (ctl >> 1) + 1;
                    int pixel_count = m_pixel_size * count;
                    if (0 == (ctl & 1))
                    {
                        m_input.Read (m_output, dst, pixel_count);
                    }
                    else
                    {
                        m_input.Read (m_output, dst, m_pixel_size);
                        Binary.CopyOverlapped (m_output, dst, dst+m_pixel_size, pixel_count - m_pixel_size);
                    }
                    dst += pixel_count;
                    x += count;
                }
                if (gap != 0)
                    m_input.Read (m_output, dst, gap);
            }
        }
    }
}
