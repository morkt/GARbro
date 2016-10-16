//! \file       ImageSPC.cs
//! \date       Tue Mar 08 19:05:11 2016
//! \brief      CRI MiddleWare compressed multi-frame image.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Cri
{
    [Export(typeof(ImageFormat))]
    public class SpcFormat : XtxFormat
    {
        public override string         Tag { get { return "SPC"; } }
        public override string Description { get { return "CRI MiddleWare compressed texture format"; } }
        public override uint     Signature { get { return 0; } }

        public SpcFormat ()
        {
            Signatures = new uint[] { 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            uint unpacked_size = stream.Signature;
            if (unpacked_size <= 0x20 || unpacked_size > 0x5000000) // ~83MB
                return null;
            using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
            using (var xtx = new BinaryStream (input, stream.Name))
                return base.ReadMetaData (xtx);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            using (var input = new SeekableStream (lzss))
            using (var xtx = new BinaryStream (input, stream.Name))
                return base.Read (xtx, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpcFormat.Write not implemented");
        }
    }
}
