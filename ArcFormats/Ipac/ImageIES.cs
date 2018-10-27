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
                var palette = ReadPalette (stream.AsStream, 0x100, PaletteFormat.RgbX);
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
    }

    [Export(typeof(ImageFormat))]
    public class IesRawFormat : ImageFormat
    {
        public override string         Tag { get { return "IES/RAW"; } }
        public override string Description { get { return "IPAC image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".ies"))
                return null;
            var header = file.ReadHeader (0x10);
            if (header.ToInt32 (12) != 0)
                return null;
            uint width  = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            int  bpp    = header.ToInt32 (8);
            if (width * height * (bpp / 8) != (file.Length - 0x414))
                return null;
            return new ImageMetaData
            {
                Width   = width,
                Height  = height,
                BPP     = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            if (32 == info.BPP)
            {
                file.Position = 0x414;
                var pixels = file.ReadBytes ((int)info.Width * (int)info.Height * 4);
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
            else if (8 == info.BPP)
            {
                file.Position = 0x14;
                var palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.RgbX);
                var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
                return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
            }
            else
                throw new InvalidFormatException ("[IES] Invalid color depth");
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IesRawFormat.Write not implemented");
        }
    }
}
