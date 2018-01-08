//! \file       ImagePMP.cs
//! \date       Thu Apr 30 20:39:26 2015
//! \brief      ScenePlayer compressed bitmap.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.ScenePlayer
{
    [Export(typeof(ImageFormat))]
    public class PmpFormat : ImageFormat
    {
        public override string         Tag { get { return "PMP"; } }
        public override string Description { get { return "ScenePlayer compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new XoredStream (file, 0x21, true))
            using (var zstream = new ZLibStream (output, CompressionMode.Compress, CompressionLevel.Level9))
                Bmp.Write (zstream, image);
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int first = stream.PeekByte() ^ 0x21;
            if (first != 0x78) // doesn't look like zlib stream
                return null;

            using (var input = new XoredStream (stream.AsStream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var bmp = new BinaryStream (zstream, stream.Name))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var input = new XoredStream (stream.AsStream, 0x21, true))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var bmp = new BinaryStream (zstream, stream.Name))
                return Bmp.Read (bmp, info);
        }
    }
}
