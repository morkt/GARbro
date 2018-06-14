//! \file       ImageHOT.cs
//! \date       2018 Jun 08
//! \brief      HDL engine image format.
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

namespace GameRes.Formats.Hdl
{
    [Export(typeof(ImageFormat))]
    public class HotFormat : ImageFormat
    {
        public override string         Tag { get { return "HOT"; } }
        public override string Description { get { return "HDL engine image format"; } }
        public override uint     Signature { get { return 0x544F48; } } // 'HOT'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if ((header[7] & 0x21) != 0x21)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP    = 15,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = new short[info.Width * info.Height];
            file.Position = 0x20;
            int dst = 0;
            while (dst < pixels.Length && file.PeekByte() != -1)
            {
                short px = file.ReadInt16();
                if (px < 0)
                {
                    px &= 0x7FFF;
                    int count = file.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                        pixels[dst++] = px;
                }
                else
                {
                    pixels[dst++] = px;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HotFormat.Write not implemented");
        }
    }
}
