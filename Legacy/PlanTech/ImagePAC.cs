//! \file       ImagePAC.cs
//! \date       2018 Jan 03
//! \brief      PLANTECH bitmap packages.
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

namespace GameRes.Formats.PlanTech
{
    [Export(typeof(ImageFormat))]
    public class PacFormat : ImageFormat
    {
        public override string         Tag { get { return "PAC/PLANTECH"; } }
        public override string Description { get { return "PLANTECH engine bitmap package"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature != 0)
                return null;
            var header = file.ReadHeader (14);
            if (!header.AsciiEqual (8, "BM"))
                return null;
            if (header.ToUInt32 (4) != header.ToUInt32 (10))
                return null;
            using (var region = new StreamRegion (file.AsStream, 8, true))
            using (var bmp = new BinaryStream (region, file.Name))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (BmpMetaData)info;
            file.Position = 8 + meta.ImageOffset;
            int stride = (((int)info.Width * info.BPP / 8) + 3) & ~3;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                format = PixelFormats.Bgr32;
            else if (16 == info.BPP)
                format = PixelFormats.Bgr565;
            else if (8 == info.BPP)
                format = PixelFormats.Gray8;
            else
                throw new InvalidFormatException();
            return ImageData.Create (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PacFormat.Write not implemented");
        }
    }
}
