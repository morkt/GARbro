//! \file       ImageBMZ.cs
//! \date       Wed Mar 04 11:48:31 2015
//! \brief      Compressed bitmap image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ImageFormat))]
    public class BmzFormat : BmpFormat
    {
        public override string         Tag { get { return "BMZ"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0x33434c5au; } } // 'ZLC3'
        public override bool      CanWrite { get { return true; } }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                base.Write (bmp, image);
                using (var output = new BinaryWriter (file, Encoding.ASCII, true))
                {
                    output.Write (Signature);
                    output.Write ((uint)bmp.Length);
                }
                bmp.Position = 0;
                using (var zstream = new ZLibStream (file, CompressionMode.Compress, CompressionLevel.Level9, true))
                    bmp.CopyTo (zstream);
            }
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[8];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
                return base.ReadMetaData (zstream);
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Seek (8, SeekOrigin.Current);
            using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
            using (var input = new SeekableStream (zstream))
                return base.Read (input, info);
        }
    }
}
