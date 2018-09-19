//! \file       ImageTM2.cs
//! \date       2018 Sep 18
//! \brief      PlayStation2 image format.
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

namespace GameRes.Formats.DigitalWorks
{
    internal class Tim2MetaData : ImageMetaData
    {
        public int  PaletteSize;
        public int  HeaderSize;
        public int  Colors;
    }

    [Export(typeof(ImageFormat))]
    public class Tim2Format : ImageFormat
    {
        public override string         Tag { get { return "TIM2"; } }
        public override string Description { get { return "PlayStation/2 image format"; } }
        public override uint     Signature { get { return 0x324D4954; } } // 'TIM2'

        public Tim2Format ()
        {
            Extensions = new string[] { "tm2", "ext" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            byte bpp = header[0x23];
            switch (bpp)
            {
            case 1: bpp = 16; break;
            case 2: bpp = 24; break;
            case 3: bpp = 32; break;
            case 5: bpp = 8; break;
            default: return null;
            }
            return new Tim2MetaData {
                Width  = header.ToUInt16 (0x24),
                Height = header.ToUInt16 (0x26),
                BPP    = bpp,
                PaletteSize = header.ToInt32 (0x14),
                HeaderSize = header.ToUInt16 (0x1C),
                Colors  = header.ToUInt16 (0x1E),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Tim2Reader (file, (Tim2MetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Tim2Format.Write not implemented");
        }
    }

    internal class Tim2Reader
    {
        IBinaryStream   m_input;
        Tim2MetaData    m_info;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public Tim2Reader (IBinaryStream input, Tim2MetaData info)
        {
            m_input = input;
            m_info = info;
            switch (info.BPP)
            {
            case 8:  Format = PixelFormats.Indexed8; break;
            case 16: Format = PixelFormats.Bgr555; break;
            case 24: Format = PixelFormats.Bgr24;  break;
            case 32: Format = PixelFormats.Bgra32; break;
            }
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x10 + m_info.HeaderSize;
            int pixel_size = m_info.BPP / 8;
            int image_size = (int)m_info.Width * (int)m_info.Height * pixel_size;
            var output = m_input.ReadBytes (image_size);
            if (pixel_size <= 8 && m_info.Colors > 0)
                Palette = ReadPalette (m_info.Colors);

            if (pixel_size >= 3)
            {
                for (int i = 0; i < image_size; i += pixel_size)
                {
                    byte r = output[i];
                    output[i] = output[i+2];
                    output[i+2] = r;
                }
            }
            return output;
        }

        BitmapPalette ReadPalette (int color_num)
        {
            var source = ImageFormat.ReadColorMap (m_input.AsStream, color_num, PaletteFormat.RgbA);
            var color_map = new Color[color_num];

            int parts = color_num / 32;
            const int blocks = 2;
            const int rows = 2;
            const int colors = 8;

            int dst = 0;
            for (int part = 0; part < parts; part++)
            for (int block = 0; block < blocks; block++)
            for (int row = 0; row < rows; row++)
            {
                int src = (part * rows * blocks + row * rows + block) * colors;
                Array.Copy (source, src, color_map, dst, colors);
                dst += colors;
            }
            return new BitmapPalette (color_map);
        }
    }
}
