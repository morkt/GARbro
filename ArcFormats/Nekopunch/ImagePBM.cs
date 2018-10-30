//! \file       ImagePBM.cs
//! \date       2018 Jan 03
//! \brief      Studio Nekopunch compressed bitmap.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.Nekopunch
{
    [Export(typeof(ImageFormat))]
    public class PbmFormat : ImageFormat
    {
        public override string         Tag { get { return "PBM"; } }
        public override string Description { get { return "Studio Nekopunch compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".pbm"))
                return null;
            var header = file.ReadHeader (8);
            if ((header[4] & 7) != 7 || !header.AsciiEqual (5, "BM"))
                return null;
            using (var bmp = OpenBitmapStream (file, header.ToUInt32 (0)))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenBitmapStream (file, file.Signature))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenBitmapStream (IBinaryStream file, uint unpacked_size)
        {
            file.Position = 4;
            Stream input = new LzssStream (file.AsStream, LzssMode.Decompress, true);
            input = new LimitStream (input, unpacked_size, StreamOption.Fill);
            return new BinaryStream (input, file.Name); 
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PbmFormat.Write not implemented");
        }
    }
}
