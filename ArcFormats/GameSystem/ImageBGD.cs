//! \file       ImageBGD.cs
//! \date       Mon Jan 16 06:43:56 2017
//! \brief      'Game System' background image format.
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

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ImageFormat))]
    public class BgdFormat : ImageFormat
    {
        public override string         Tag { get { return "BGD"; } }
        public override string Description { get { return "'GameSystem' background image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature+0x10 != file.Length)
                return null;
            var header = file.ReadHeader (0x10);
            uint width = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int total_pixels = (int)info.Width * (int)info.Height;
            var pixels = new byte[total_pixels * 3];
            int count = total_pixels >> 1;
            int dst = 0;
            int s1 = 1, s2 = 1, s3 = 1;
            int r = 0, g = 0, b = 0;
            file.Position = 0x10;
            for (int i = 0; i < count; ++i)
            {
                byte c = file.ReadUInt8();
                s1 = s1 << 4 | c & 0xF;
                s2 = s2 << 4 | (c >> 4) & 0xF;
                b += RgbShift[s1];
                g += RgbShift[s2];
                c = file.ReadUInt8();
                s3 = s3 << 4 | c & 0xF;
                r += RgbShift[s3];
                pixels[dst++] = (byte)b;
                pixels[dst++] = (byte)g;
                pixels[dst++] = (byte)r;
                s3 = ShiftTable[s3] << 4 | (c >> 4) & 0xF;
                c = file.ReadUInt8();
                s1 = ShiftTable[s1] << 4 | c & 0xF;
                s2 = ShiftTable[s2] << 4 | (c >> 4) & 0xF;
                b += RgbShift[s1];
                g += RgbShift[s2];
                r += RgbShift[s3];
                s1 = ShiftTable[s1];
                s2 = ShiftTable[s2];
                s3 = ShiftTable[s3];
                pixels[dst++] = (byte)b;
                pixels[dst++] = (byte)g;
                pixels[dst++] = (byte)r;
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, (int)info.Width*3);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgdFormat.Write not implemented");
        }

        static readonly byte[] ShiftTable = {
            0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1,
            0, 0, 1, 1, 2, 2, 1, 1, 0, 0, 1, 1, 2, 2, 1, 1,
            1, 1, 2, 2, 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 1, 1,
        };
        static readonly short[] RgbShift = {
             1,     2,     4,     8,  0x10,  0x26,  0x50,  0xAA,
            -1,    -2,    -4,    -8, -0x10, -0x26, -0x50, -0xAA,
             2,     4,     6,  0x0C,  0x18,  0x30,  0x60,  0xC0,
            -2,    -4,    -6, -0x0C, -0x18, -0x30, -0x60, -0xC0,
             5,  0x0A,  0x14,  0x1E,  0x32,  0x50,  0x82,  0xD2,
            -5, -0x0A, -0x14, -0x1E, -0x32, -0x50, -0x82, -0xD2,
        };
    }
}
