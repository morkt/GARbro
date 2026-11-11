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

using GameRes.Formats.Strings;
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
        public byte Alpha;
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
            Settings = new[] { AlphaFormat };
        }

        FixedSetSetting AlphaFormat = new FixedSetSetting(Properties.Settings.Default)
        {
            Name = "TIM2AlphaFormat",
            Text = arcStrings.Tim2AlphaFormat,
            ValuesSet = new[] { "No Alpha", "RGBX", "RGBA" },
        };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            byte bpp = header[0x23];
            switch (bpp)
            {
            case 1: bpp = 16; break;
            case 2: bpp = 24; break;
            case 3: bpp = 32; break;
            case 4: bpp = 4; break; //16color
            case 5: bpp = 8; break;
            default: return null;
            }
            byte alpha;
            switch (AlphaFormat.Get<String>())
            {
                case "No Alpha": alpha = 0; break;
                case "RGBX": alpha = 7; break;
                case "RGBA":
                default: alpha = 8; break;
            }
            return new Tim2MetaData {
                Width  = header.ToUInt16 (0x24),
                Height = header.ToUInt16 (0x26),
                BPP    = bpp,
                PaletteSize = header.ToInt32 (0x14),
                HeaderSize = header.ToUInt16 (0x1C),
                Colors  = header.ToUInt16 (0x1E),
                Alpha = alpha, //header.ToUInt16(0x30) == 0?// not so sure, there will be omissions
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
                case 4:  Format = PixelFormats.Indexed4; break;
                case 8:  Format = PixelFormats.Indexed8; break;
                case 16: Format = PixelFormats.Bgr555; break;
                case 24: Format = PixelFormats.Bgr24;  break;
                case 32: Format = PixelFormats.Bgra32; break;
            }
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x10 + m_info.HeaderSize;
            double pixel_size = (double)m_info.BPP / 8;
            int image_size = (int)((int)m_info.Width * (int)m_info.Height * pixel_size);
            var output = m_input.ReadBytes (image_size);
            if (pixel_size <= 1 && m_info.Colors > 0) // Indexed images
                Palette = ReadPalette (m_info.Colors, m_info.Alpha);

            // Handle 24bpp and 32bpp (Direct Color)
            if (pixel_size == 3 || (pixel_size == 4 && m_info.Alpha == 8))
            {
                for (int i = 0; i < image_size; i += (int)pixel_size)
                {
                    // Swap R and B channels
                    byte r = output[i];
                    output[i] = output[i+2];
                    output[i+2] = r;

                    // Fix Alpha for 32bpp (Double Alpha Scaling)
                    if (pixel_size == 4)
                    {
                        byte a = output[i + 3];
                        if (a > 0)
                        {
                            output[i + 3] = (byte)Math.Min(255, a * 2);
                        }
                    }
                }
            }
            // Handle specific RGBX (Alpha 7) case
            else if (pixel_size == 4 && m_info.Alpha == 7)
            {
                for (int i = 0; i < image_size; i += 4)
                {
                    byte r = output[i];
                    output[i] = output[i + 2];
                    output[i + 2] = r;
                    if (output[i + 3] >= byte.MaxValue / 2)
                        output[i + 3] = byte.MaxValue;
                    else
                        output[i + 3] = (byte)(output[i + 3] << 1);
                }
            }
            // Handle No Alpha case
            else if (pixel_size == 4 && m_info.Alpha == 0)
            {
                for (int i = 0; i < image_size; i += 4)
                {
                    byte r = output[i];
                    output[i] = output[i + 2];
                    output[i + 2] = r;
                    output[i + 3] = byte.MaxValue;
                }
            }
            return output;
        }

        BitmapPalette ReadPalette (int color_num, byte X_A = 8)
        {
            var source = ImageFormat.ReadColorMap (m_input.AsStream,
                color_num, X_A == 7 ? PaletteFormat.RgbA7 : X_A == 0 ? PaletteFormat.RgbX : PaletteFormat.RgbA);

            // Scaled Alpha for Indexed Palettes (KID/PS2 Fix)
            if (X_A != 0)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    byte a = source[i].A;
                    if (a > 0)
                    {
                        byte newAlpha = (byte)Math.Min(255, a * 2);
                        source[i] = Color.FromArgb(newAlpha, source[i].R, source[i].G, source[i].B);
                    }
                }
            }

            var color_map = new Color[color_num];

            if (color_num == 16){
                Array.Copy(source, 0, color_map, 0, 16);
                return new BitmapPalette(color_map);
            }

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
