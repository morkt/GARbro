//! \file       ImageKBP.cs
//! \date       2026-09-07
//! \brief      SVIU System image format.
//
// Copyright (C) 2026 by morkt
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
using System.Windows.Media;

namespace GameRes.Formats.Sviu
{
    [Export(typeof(ImageFormat))]
    public class KbpFormat : ImageFormat
    {
        public override string         Tag { get { return "KBP"; } }
        public override string Description { get { return "SVIU System image format"; } }
        public override uint     Signature { get { return 0x3150424B; } } // 'KBP1'

        public KbpFormat ()
        {
            Signatures = new uint[] { 0x3150424B, 0x3250424B };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            uint width = header.ToUInt16 (0x8);
            uint height = header.ToUInt16 (0xC);

            long remainder;
            long bytes_per_pixel = Math.DivRem (file.Length - header.ToInt32 (0x10), width * height, out remainder);
            if (remainder != 0 || (bytes_per_pixel != 1 && bytes_per_pixel != 4))
                return null;

            return new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = (int)bytes_per_pixel * 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            file.Position = file.ReadUInt32(); // 0x14
            var pixels = new byte[info.iWidth * info.iHeight * info.BPP / 8];
            file.Read (pixels, 0, pixels.Length);
            return ImageData.Create (info, info.BPP == 8 ? PixelFormats.Gray8 : PixelFormats.Bgr32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KbpFormat.Write not implemented");
        }
    } 
}
