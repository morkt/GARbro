//! \file       ImageRBM.cs
//! \date       2017 Dec 31
//! \brief      Mutation compressed image.
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
using GameRes.Utility;

namespace GameRes.Formats.Mutation
{
    [Export(typeof(ImageFormat))]
    public class RbmFormat : ImageFormat
    {
        public override string         Tag { get { return "RBM"; } }
        public override string Description { get { return "Mutation compressed image"; } }
        public override uint     Signature { get { return 0x004D4252; } } // 'RBM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (10);
            return new ImageMetaData {
                Width = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 10;
            int stride = (int)info.Width * 3;
            var pixels = new byte[stride * (int)info.Height];
            int dst = 0;
            byte bit = 0;
            byte bit_mask = 0;
            while (dst < pixels.Length)
            {
                bit_mask >>= 1;
                if (0 == bit_mask)
                {
                    bit = file.ReadUInt8();
                    bit_mask = 0x80;
                }
                if (0 != (bit & bit_mask))
                {
                    int offset = file.ReadUInt16();
                    int count = ((offset & 0xF) + 1) * 3;
                    offset = ((offset >> 4) + 1) * 3;
                    Binary.CopyOverlapped (pixels, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    file.Read (pixels, dst, 3);
                    dst += 3;
                }
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RbmFormat.Write not implemented");
        }
    }
}
