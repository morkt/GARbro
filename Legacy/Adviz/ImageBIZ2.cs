//! \file       ImageBIZ2.cs
//! \date       2018 Feb 11
//! \brief      ADVIZ engine compressed image.
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
using GameRes.Compression;

// [000225][Sorciere] Karei
// [011012][Ange] Nyuunyuu

namespace GameRes.Formats.Adviz
{
    [Export(typeof(ImageFormat))]
    public class Biz2Format : ImageFormat
    {
        public override string         Tag { get { return "BIZ/2"; } }
        public override string Description { get { return "ADVIZ engine compressed image"; } }
        public override uint     Signature { get { return 0x325A4942; } } // 'BIZ2'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            using (var input = new BinaryStream (lzss, file.Name))
            {
                int stride = info.iWidth * 3;
                var rgb = new byte[stride * info.Height];
                if (rgb.Length != input.Read (rgb, 0, rgb.Length))
                    throw new InvalidFormatException();
                if (input.PeekByte() != -1) // possible alpha channel
                {
                    var alpha = input.ReadBytes (rgb.Length);
                    if (alpha.Length == rgb.Length)
                    {
                        int stride32bpp = info.iWidth * 4;
                        var rgba = new byte[stride32bpp * info.iHeight];
                        int src = 0;
                        int dst = 0;
                        while (src < rgb.Length)
                        {
                            rgba[dst++] = rgb[src  ];
                            rgba[dst++] = rgb[src+1];
                            rgba[dst++] = rgb[src+2];
                            rgba[dst++] = alpha[src]; // presumably it's grayscale and R/G/B values are equal
                            src += 3;
                        }
                        return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, rgba, stride32bpp);
                    }
                }
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, rgb, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BizFormat.Write not implemented");
        }
    }
}
