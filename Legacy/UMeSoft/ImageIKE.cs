//! \file       ImageIKE.cs
//! \date       2018 Mar 22
//! \brief      ike-compressed image.
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
using System.Windows.Media;

namespace GameRes.Formats.UMeSoft
{
    internal class IkeMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class IkeFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/IKE"; } }
        public override string Description { get { return "ike-compressed bitmap"; } }
        public override uint     Signature { get { return 0x6B69899D; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x11);
            if (!header.AsciiEqual (2, "ike") || !header.AsciiEqual (0xF, "BM"))
                return null;
            int unpacked_size = IkeReader.DecodeSize (header[10], header[11], header[12]);
            using (var bmp = IkeReader.CreateStream (file, 0x36))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new IkeMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (IkeMetaData)info;
            using (var bmp = IkeReader.CreateStream (file, meta.UnpackedSize))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IkeFormat.Write not implemented");
        }
    }
}
