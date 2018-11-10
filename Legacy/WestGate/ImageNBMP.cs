//! \file       ImageNBMP.cs
//! \date       2017 Dec 15
//! \brief      West Gate bitmap.
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ImageFormat))]
    public class NbmpFormat : ImageFormat
    {
        public override string         Tag { get { return "NBMP"; } }
        public override string Description { get { return "West Gate bitmap format"; } }
        public override uint     Signature { get { return 0x504D424E; } } // 'NBMP'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x2C);
            if (header.ToInt32 (4) != 0x28)
                return null;
            int bpp = header.ToInt16 (0x12);
            if (bpp != 24 && bpp != 32 && bpp != 8)
                return null;
            return new ImageMetaData {
                Width = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = ((int)info.Width * info.BPP / 8 + 3) & ~3;
            file.Position = 0x2C;
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (file.AsStream);
            var pixels = file.ReadBytes (stride * (int)info.Height);
            PixelFormat format = 8 == info.BPP ? PixelFormats.Indexed8
                              : 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgr32;
            return ImageData.CreateFlipped (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NbmpFormat.Write not implemented");
        }
    }
}
