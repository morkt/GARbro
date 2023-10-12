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
    public class MbImageFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/MB"; } }
        public override string Description { get { return "Obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public MbImageFormat ()
        {
            Extensions = new[] { "bmp", "gra", "xxx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int c1 = stream.ReadByte();
            int c2 = stream.ReadByte();
            // MB/MC/MK/CL
            switch (c1)
            {
            case 'M':
                if ('B' != c2 && 'C' != c2 && 'K' != c2)
                    return null;
                break;
            case 'C':
                if ('L' != c2)
                    return null;
                break;
            default:
                return null;
            }
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.Read (bmp, info);
        }

        protected IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[2] { (byte)'B', (byte)'M' };
            Stream stream = new StreamRegion (input.AsStream, 2, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                Bmp.Write (bmp, image);
                file.WriteByte ((byte)'M');
                file.WriteByte ((byte)'B');
                bmp.Position = 2;
                bmp.CopyTo (file);
            }
        }
    }
}
