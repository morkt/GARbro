//! \file       Image1.cs
//! \date       2018 Apr 20
//! \brief      Pisckiss encrypted bitmap.
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

// [040220][pisckiss] SCHREI-TEN

namespace GameRes.Formats.Pisckiss
{
    [Export(typeof(ImageFormat))]
    public class Bm1Format : ImageFormat
    {
        public override string         Tag { get { return "BMP/PISCKISS"; } }
        public override string Description { get { return "Pisckiss encrypted bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public Bm1Format ()
        {
            Extensions = new[] { "1" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (5);
            if ((header[0] & 0x42) != 0)
                return null;
            int shift = 0;
            uint width = 0;
            uint height = 0;
            for (int i = 0; i < 4; ++i)
            {
                uint v = header[i+1];
                width  |= ((v >> 4 * (i & 1)) & 0xF) << shift;
                height |= ((v >> 4 * (~i & 1)) & 0xF) << shift;
                shift += 4;
            }
            if (0 == width || 0 == height)
                return null;
            uint stride = (width * 3 + 3) & ~3u;
            uint length = 5 + stride * height;
            if (length != file.Length)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 5;
            int stride = ((int)info.Width * 3 + 3) & ~3;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Bm1Format.Write not implemented");
        }
    }
}
