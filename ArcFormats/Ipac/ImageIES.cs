//! \file       ImageIES.cs
//! \date       Sat Jul 23 16:04:34 2016
//! \brief      IPAC image format.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.BaseUnit
{
    [Export(typeof(ImageFormat))]
    public class IesFormat : ImageFormat
    {
        public override string         Tag { get { return "IES"; } }
        public override string Description { get { return "IPAC image format"; } }
        public override uint     Signature { get { return 0x32534549; } } // 'IES2'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            return new ImageMetaData
            {
                Width   = header.ToUInt32 (0x08),
                Height  = header.ToUInt32 (0x0C),
                BPP     = header.ToInt32  (0x10),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            if (24 == info.BPP)
            {
                stream.Position = 0x420;
                var rgb = new byte[info.Width * info.Height * 3];
                if (rgb.Length != stream.Read (rgb, 0, rgb.Length))
                    throw new EndOfStreamException();
                var alpha = new byte[info.Width * info.Height];
                if (alpha.Length != stream.Read (alpha, 0, alpha.Length))
                    throw new EndOfStreamException();
                var pixels = new byte[info.Width * info.Height * 4];
                int dst = 0;
                int src_alpha = 0;
                for (int src = 0; src < rgb.Length; )
                {
                    byte a = alpha[src_alpha++];
                    pixels[dst++] = rgb[src++];
                    pixels[dst++] = rgb[src++];
                    pixels[dst++] = rgb[src++];
                    pixels[dst++] = a;
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
            else if (8 == info.BPP)
            {
                stream.Position = 0x20;
                var palette = ReadPalette (stream.AsStream);
                var pixels = new byte[info.Width * info.Height];
                if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
            }
            else
                throw new InvalidFormatException ("[IES] Invalid color depth");
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IesFormat.Write not implemented");
        }

        BitmapPalette ReadPalette (Stream input)
        {
            var palette_data = new byte[0x400];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            var palette = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c], palette_data[c+1], palette_data[c+2]);
            }
            return new BitmapPalette (palette);
        }
    }
}
