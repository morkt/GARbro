//! \file       ImageGRD.cs
//! \date       Sun Apr 12 21:05:44 2015
//! \brief      Silky's GRD image format.
//
// Copyright (C) 2015-2017 by morkt
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

namespace GameRes.Formats.Silky
{
    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD"; } }
        public override string Description { get { return "Silky's compressed bitmap format"; } }
        public override uint     Signature { get { return 0x5f504d43u; } } // 'CMP_'

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrdFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var bmp = DecompressStream (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = DecompressStream (stream))
                return Bmp.Read (bmp, info);
        }

        internal IBinaryStream DecompressStream (IBinaryStream stream)
        {
            stream.Position = 12;
            var input = new LzssStream (stream.AsStream, LzssMode.Decompress, true);
            input.Config.FrameFill = 0x20;
            return new BinaryStream (input, stream.Name);
        }
    }
}
