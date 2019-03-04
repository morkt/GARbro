//! \file       ImageFGP.cs
//! \date       2019 Feb 28
//! \brief      FazeX ADV System image.
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
using GameRes.Compression;

// [011130][Malt] Emblem

namespace GameRes.Formats.FazeX
{
    [Export(typeof(ImageFormat))]
    public class FgpFormat : ImageFormat
    {
        public override string         Tag { get { return "FGP"; } }
        public override string Description { get { return "FazeX ADV System image format"; } }
        public override uint     Signature { get { return 0x455A4146; } } // 'FAZEX_GRAPHIC_FILE'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1B);
            if (!header.AsciiEqual (0, "FAZEX_GRAPHIC_FILE"))
                return null;
            return new ImageMetaData
            {
                Width = header.ToUInt32 (0x13),
                Height = header.ToUInt32 (0x17),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x1B;
            int plane_size = info.iWidth * info.iHeight;
            var pixels = new byte[plane_size * 4];
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var plane = new byte[plane_size];
                int dst;
                for (int c = 0; c < 3; ++c)
                {
                    input.Read (plane, 0, plane_size);
                    dst = c;
                    for (int src = 0; src < plane_size; ++src)
                    {
                        pixels[dst] = plane[src];
                        dst += 4;
                    }
                }
                // alpha channel is inverted
                input.Read (plane, 0, plane_size);
                dst = 3;
                for (int src = 0; src < plane_size; ++src)
                {
                    pixels[dst] = (byte)(0xFF - plane[src]);
                    dst += 4;
                }
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, info.iWidth * 4);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FgpFormat.Write not implemented");
        }
    }
}
