//! \file       ImageJBP.cs
//! \date       2018 Aug 27
//! \brief      SVIU System image format.
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

namespace GameRes.Formats.Sviu
{
    [Export(typeof(ImageFormat))]
    public class JbpFormat : ImageFormat
    {
        public override string         Tag { get { return "JBP"; } }
        public override string Description { get { return "SVIU System image format"; } }
        public override uint     Signature { get { return 0x3150424A; } } // 'JBP1'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            return new ImageMetaData {
                Width  = header.ToUInt16 (0x10),
                Height = header.ToUInt16 (0x12),
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var input = file.ReadBytes ((int)file.Length);
            var reader = new Purple.JbpReader (input, 0);
            var pixels = reader.Unpack();
            return ImageData.Create (info, PixelFormats.Bgr32, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("JbpFormat.Write not implemented");
        }
    }
}
