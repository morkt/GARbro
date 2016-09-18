//! \file       ImageMB.cs
//! \date       Sun Sep 18 19:05:50 2016
//! \brief      Ancient obfuscated BMP image.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class MbImageFormat : BmpFormat
    {
        public override string         Tag { get { return "BMP/MB"; } }
        public override string Description { get { return "Obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            int c1 = stream.ReadByte();
            int c2 = stream.ReadByte();
            if ('M' != c1 || 'B' != c2)
                return null;
            using (var bmp = OpenAsBitmap (stream))
                return base.ReadMetaData (bmp);
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (stream))
                return base.Read (bmp, info);
        }

        Stream OpenAsBitmap (Stream input)
        {
            var header = new byte[2] { (byte)'B', (byte)'M' };
            input = new StreamRegion (input, 2, true);
            return new PrefixStream (header, input);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                base.Write (bmp, image);
                file.WriteByte ((byte)'M');
                file.WriteByte ((byte)'B');
                bmp.Position = 2;
                bmp.CopyTo (file);
            }
        }
    }
}
