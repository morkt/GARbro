//! \file       ImageMBP.cs
//! \date       2017 Dec 31
//! \brief      h.m.p bitmap format.
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

// [000421][Sepia] Rinjin Mousou ~Danchizoku no Hirusagari~
// [000623][Sweet] Depaga ~Service Angel~

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    public class MbpFormat : ImageFormat
    {
        public override string         Tag { get { return "MBP"; } }
        public override string Description { get { return "h.m.p bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".MBP"))
                return null;
            var header = file.ReadHeader (8);
            uint w = header.ToUInt32 (0);
            uint h = header.ToUInt32 (4);
            if (8 + w * h * 2 != file.Length)
                return null;
            return new ImageMetaData {
                Width = w,
                Height = h,
                BPP = 15,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int stride = (int)info.Width * 2;
            var pixels = file.ReadBytes (stride * (int)info.Height);
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MbpFormat.Write not implemented");
        }
    }
}
