//! \file       ImagePTI.cs
//! \date       Sun Oct 11 02:40:54 2015
//! \brief      Custom BMP format.
//
// Copyright (C) 2015 by morkt
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ImageFormat))]
    public class PtiFormat : BmpFormat
    {
        public override string         Tag { get { return "PTI"; } }
        public override string Description { get { return "Custom BMP image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = ReadHeader (stream);
            if (null == header)
                return null;
            using (var bmp = new MemoryStream (header))
                return base.ReadMetaData (bmp);
        }

        byte[] ReadHeader (Stream stream)
        {
            var header = new byte[0x36];
            if (0x10 != stream.Read (header, 0, 0x10)
                || 'B' != header[0] || 'M' != header[1]
                || 0 != LittleEndian.ToUInt16 (header, 0xE)
                || 0x28 != stream.Read (header, 0xE, 0x28)
                || 0x28 != header[0xE])
                return null;
            return header;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            uint length = (uint)(stream.Length - 0x38);
            var image = new byte[length+0x38];
            stream.Read (image, 0, 0x10);
            stream.Read (image, 0xE, (int)length+0x28);
            if (24 == info.BPP && length+2 == info.Width * info.Height * 3)
            {
                image[image.Length-2] = 0xFF;
                image[image.Length-1] = 0xFF;
                length += 2;
            }
            LittleEndian.Pack (length+0x36, image, 2);
            using (var bmp = new MemoryStream (image))
                return base.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PtiFormat.Write not implemented");
        }
    }
}
