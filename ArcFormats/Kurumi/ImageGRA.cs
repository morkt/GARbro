//! \file       ImageGRA.cs
//! \date       Tue Mar 28 06:35:39 2017
//! \brief      Virgin Snow image format.
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
using GameRes.Compression;

namespace GameRes.Formats.Kurumi
{
    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag { get { return "GRA/VS"; } }
        public override string Description { get { return "Virgin Snow image format"; } }
        public override uint     Signature { get { return 0x67726956; } } // 'Virg'

        static readonly byte[] Key = { 0x5A, 0xA5 };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (!header.AsciiEqual ("Virgin Snow Compressed Data 1.0"))
                return null;
            using (var bmp = UnpackStream (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x20;
            using (var bmp = UnpackStream (file))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream UnpackStream (IBinaryStream file)
        {
            Stream input = new ByteStringEncryptedStream (file.AsStream, Key, true);
            input = new ZLibStream (input, CompressionMode.Decompress);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GraFormat.Write not implemented");
        }
    }
}
