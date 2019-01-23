//! \file       ImageLPG.cs
//! \date       2019 Jan 15
//! \brief      Kogado Studio image format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Kogado
{
    [Export(typeof(ImageFormat))]
    public class LpgFormat : ImageFormat
    {
        public override string         Tag { get { return "LPG"; } }
        public override string Description { get { return "Kogado Studio image format"; } }
        public override uint     Signature { get { return 1; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("LPG"))
                return null;
            var header = file.ReadHeader (0x1C);
            int bpp = header.ToInt32 (4);
            uint width  = header.ToUInt32 (0x10);
            uint height = header.ToUInt32 (0x14);
            if (bpp != 32 && bpp != 24
                || 0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x1C;
            var palette = ReadPalette (file.AsStream);
            if (info.BPP != 32)
            {
                var pixels = new byte[info.iWidth * info.iHeight];
                file.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, info.iWidth);
            }
            else
            {
                int stride = info.iWidth * 4;
                var pixels = new byte[stride * info.iHeight];
                var colormap = palette.Colors;
                for (int dst = 0; dst < pixels.Length; dst += 4)
                {
                    byte c = file.ReadUInt8();
                    pixels[dst  ] = colormap[c].B;
                    pixels[dst+1] = colormap[c].G;
                    pixels[dst+2] = colormap[c].R;
                    pixels[dst+3] = file.ReadUInt8();
                }
                return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LpgFormat.Write not implemented");
        }
    }
}
