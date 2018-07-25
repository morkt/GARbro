//! \file       ImageJPG.cs
//! \date       2018 Jul 15
//! \brief      Gaia obfuscated JPEG image.
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

namespace GameRes.Formats.Gaia
{
    [Export(typeof(ImageFormat))]
    public class HiddenJpegFormat : ImageFormat
    {
        public override string         Tag { get { return "JPG/HIDDEN"; } }
        public override string Description { get { return "Gaia obfuscated JPEG image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if ((file.Signature & 0xFFFFFF) != 0xFDFF00)
                return null;
            using (var jpeg = OpenAsJpeg (file))
                return Jpeg.ReadMetaData (jpeg);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var jpeg = OpenAsJpeg (file))
                return Jpeg.Read (jpeg, info);
        }

        IBinaryStream OpenAsJpeg (IBinaryStream file)
        {
            var input = new StreamRegion (file.AsStream, 100, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HiddenJpegFormat.Write not implemented");
        }
    }
}
