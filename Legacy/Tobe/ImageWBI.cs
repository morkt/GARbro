//! \file       ImageWBI.cs
//! \date       2023 Oct 18
//! \brief      TOBE image format.
//
// Copyright (C) 2023 by morkt
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

// [000915][TOBE] One's Own [or very]

namespace GameRes.Formats.Tobe
{
    internal class WbiMetaData : ImageMetaData
    {
        public byte RleCode;
    }

    [Export(typeof(ImageFormat))]
    public class WbiFormat : ImageFormat
    {
        public override string         Tag => "WBI";
        public override string Description => "TOBE image format";
        public override uint     Signature => 0x2D494257; // 'WBI-'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (!header.AsciiEqual (4, "V1.00\0"))
                return null;
            return new WbiMetaData
            {
                Width = header.ToUInt16 (0xE),
                Height = header.ToUInt16 (0x10),
                BPP = 24,
                RleCode = header[0x1C],
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var wbi = (WbiMetaData)info;
            file.Position = 0x20;
            int stride = wbi.iWidth * 3;
            var pixels = new byte[stride * wbi.iHeight];
            int dst = 0;
            bool skip = false;
            byte r = 0, g = 0, b = 0;
            int count = 0;
            while (dst < pixels.Length)
            {
                if (count <= 0)
                {
                    b = file.ReadUInt8();
                    if (skip)
                    {
                        file.ReadByte();
                        skip = false;
                    }
                    g = file.ReadUInt8();
                    r = file.ReadUInt8();
                    count = 1;
                    if (wbi.RleCode == file.PeekByte())
                    {
                        count = file.ReadUInt16() >> 8;
                        if (count <= 0)
                        {
                            count = 1;
                            file.Seek (-2, SeekOrigin.Current);
                            skip = true;
                        }
                    }
                }
                --count;
                pixels[dst++] = b;
                pixels[dst++] = g;
                pixels[dst++] = r;
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("WbiFormat.Write not implemented");
        }
    }
}
