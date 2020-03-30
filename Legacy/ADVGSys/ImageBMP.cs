//! \file       ImageBMP.cs
//! \date       2019 May 29
//! \brief      Compressed bitmap format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.AdvgSys
{
    [Export(typeof(ImageFormat))]
    public class AdvgFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/ADVG"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xA);
            if (!((header[4] & 0xF) == 0xF && header.AsciiEqual (5, "BM")))
                return null;
            using (var input = OpenBitmapStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenBitmapStream (file, true))
                return Bmp.Read (input, info);
        }

        internal IBinaryStream OpenBitmapStream (IBinaryStream input, bool seekable = false)
        {
            input.Position = 4;
            Stream bmp = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            if (seekable)
                bmp = new SeekableStream (bmp);
            return new BinaryStream (bmp, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AdvgFormat.Write not implemented");
        }
    }
}
