//! \file       ImageGGF.cs
//! \date       2018 Jan 02
//! \brief      BMP-embedded image.
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

namespace GameRes.Formats.YellowCap
{
    [Export(typeof(ImageFormat))]
    public class GgfFormat : ImageFormat
    {
        public override string         Tag { get { return "GGF"; } }
        public override string Description { get { return "BMP-embedded image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (10);
            if (!header.AsciiEqual (8, "BM"))
                return null;
            using (var bmp = OpenBmpStream (file))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info || info.Width != header.ToUInt32 (0) || info.Height != header.ToUInt32 (4))
                    return null;
                return info;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenBmpStream (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenBmpStream (IBinaryStream file)
        {
            var part = new StreamRegion (file.AsStream, 8, true);
            return new BinaryStream (part, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GgfFormat.Write not implemented");
        }
    }
}
