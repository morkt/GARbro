//! \file       ImageRMT.cs
//! \date       2017 Dec 11
//! \brief      Ai5 engine compressed image format.
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
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Elf
{
    [Export(typeof(ImageFormat))]
    public class RmtFormat : ImageFormat
    {
        public override string         Tag { get { return "RMT"; } }
        public override string Description { get { return "Ai5 engine compressed image format"; } }
        public override uint     Signature { get { return 0x20544D52; } } // 'RMT '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                OffsetX = header.ToInt32 (4),
                OffsetY = header.ToInt32 (8),
                Width   = header.ToUInt32 (0xC),
                Height  = header.ToUInt32 (0x10),
                BPP     = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = (int)info.Width * 4;
            var pixels = new byte[stride * (int)info.Height];
            file.Position = 0x14;
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                input.Read (pixels, 0, pixels.Length);
            for (int i = 4; i < stride; i += 4)
            {
                pixels[i  ] += pixels[i-4];
                pixels[i+1] += pixels[i-3];
                pixels[i+2] += pixels[i-2];
                pixels[i+3] += pixels[i-1];
            }
            for (int i = stride; i < pixels.Length; i += 4)
            {
                pixels[i  ] += pixels[i-stride];
                pixels[i+1] += pixels[i-stride+1];
                pixels[i+2] += pixels[i-stride+2];
                pixels[i+3] += pixels[i-stride+3];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RmtFormat.Write not implemented");
        }
    }
}
