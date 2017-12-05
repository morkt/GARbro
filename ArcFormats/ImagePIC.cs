//! \file       ImagePIC.cs
//! \date       2017 Dec 04
//! \brief      Soft House Sprite modified bitmap image.
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
using GameRes.Utility;

namespace GameRes.Formats.Sprite
{
    [Export(typeof(ImageFormat))]
    public class PicFormat : BmpFormat
    {
        public override string         Tag { get { return "PIC/SPRITE"; } }
        public override string Description { get { return "Soft House Sprite bitmap format"; } }
        public override uint     Signature { get { return 0x434950; } } // 'PIC'
        public override bool      CanWrite { get { return true; } }

        const int HeaderSize = 10;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var bmp = OpenAsBitmap (file))
                return base.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (file))
                return base.Read (bmp, info);
        }

        IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[HeaderSize];
            header[0] = (byte)'B';
            header[1] = (byte)'M';
            LittleEndian.Pack ((uint)input.Length, header, 2);
            Stream stream = new StreamRegion (input.AsStream, HeaderSize, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                base.Write (bmp, image);
                var header = new byte[HeaderSize];
                header[0] = (byte)'P';
                header[1] = (byte)'I';
                header[2] = (byte)'C';
                file.Write (header, 0, HeaderSize);
                bmp.Position = HeaderSize;
                bmp.CopyTo (file);
            }
        }
    }
}
