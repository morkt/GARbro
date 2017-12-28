//! \file       ImageGR1.cs
//! \date       2017 Dec 28
//! \brief      LZSS-compressed bitmap.
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
using GameRes.Compression;

namespace GameRes.Formats.AnotherRoom
{
    [Export(typeof(ImageFormat))]
    public class Gr1Format : ImageFormat
    {
        public override string         Tag { get { return "GR1/LUVL"; } }
        public override string Description { get { return "AnotherRoom compressed bitmap"; } }
        public override uint     Signature { get { return 0x4C56554C; } } // 'LUVL'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24);
            if (!header.AsciiEqual (4, "LATIO"))
                return null;
            return new ImageMetaData {
                Width = header.ToUInt32 (0x1C),
                Height = header.ToUInt32 (0x20),
                BPP = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x24;
            int stride = (int)info.Width * 2;
            var pixels = new byte[stride * (int)info.Height];
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                input.Read (pixels, 0, pixels.Length);
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gr1Format.Write not implemented");
        }
    }
}
