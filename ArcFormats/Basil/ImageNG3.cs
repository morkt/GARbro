//! \file       ImageNG3.cs
//! \date       2018 Jul 12
//! \brief      BasiL image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Basil
{
    [Export(typeof(ImageFormat))]
    public class Ng3Format : ImageFormat
    {
        public override string         Tag { get { return "NG3"; } }
        public override string Description { get { return "BasiL image format"; } }
        public override uint     Signature { get { return 0x33474E; } } // 'NG3'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xC);
            return new ImageMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0xC;
            var palette = ReadColorMap (file.AsStream, 256, PaletteFormat.Bgr);
            int stride = (int)info.Width * 3;
            var pixels = new byte[stride * (int)info.Height];
            int dst = 0;
            while (dst < pixels.Length)
            {
                int ctl = file.PeekByte();
                if (-1 == ctl)
                    break;
                if (1 == ctl)
                {
                    file.ReadByte();
                    int idx = file.ReadByte();
                    var color = palette[idx];
                    pixels[dst  ] = color.B;
                    pixels[dst+1] = color.G;
                    pixels[dst+2] = color.R;
                    dst += 3;
                }
                else if (2 == ctl)
                {
                    file.ReadByte();
                    int idx = file.ReadByte();
                    int count = file.ReadByte();
                    var color = palette[idx];
                    for (int i = 0; i < count; ++i)
                    {
                        pixels[dst  ] = color.B;
                        pixels[dst+1] = color.G;
                        pixels[dst+2] = color.R;
                        dst += 3;
                    }
                }
                else
                {
                    file.Read (pixels, dst, 3);
                    dst += 3;
                }
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ng3Format.Write not implemented");
        }
    }
}
