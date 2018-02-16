//! \file       ImageCFP.cs
//! \date       2017 Nov 30
//! \brief      Tail image format.
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

namespace GameRes.Formats.Tail
{
    [Export(typeof(ImageFormat))]
    public class CfpFormat : ImageFormat
    {
        public override string         Tag { get { return "CFP"; } }
        public override string Description { get { return "Tail image format"; } }
        public override uint     Signature { get { return 0x20424552; } } // 'REB '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            return new ImageMetaData {
                Width = header.ToUInt32 (0x14),
                Height = header.ToUInt32 (0x18),
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            int stride = ((int)info.Width * 3 + 3) & -4;
            int width = ((int)info.Width + 1) & -2;
            int height = (int)info.Height;
            int total = width * height;
            int packed_size = total * 3;
            file.Position = file.Length - packed_size;
            var input = file.ReadBytes (packed_size);
            var pixels = new byte[stride*height];
            int src1 = 0;
            int src2 = total / 2;
            int src3 = total;
            int src4 = src2 + total;
            int src5 = total * 2;
            int src6 = src5 + total / 2;
            for (int dst_row = stride * (height - 1); dst_row >= 0; dst_row -= stride)
            {
                int dst = dst_row;
                for (int x = 0; x < width; x += 2)
                {
                    pixels[dst  ]  = (byte)(input[src2  ] & 0x0F | input[src1  ] << 4);
                    pixels[dst+3]  = (byte)(input[src1++] & 0xF0 | input[src2++] >> 4);
                    pixels[dst+1]  = (byte)(input[src4  ] & 0x0F | input[src3  ] << 4);
                    pixels[dst+4]  = (byte)(input[src3++] & 0xF0 | input[src4++] >> 4);
                    pixels[dst+2]  = (byte)(input[src6  ] & 0x0F | input[src5  ] << 4);
                    pixels[dst+5]  = (byte)(input[src5++] & 0xF0 | input[src6++] >> 4);
                    dst += 6;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CfpFormat.Write not implemented");
        }
    }

    internal class CfpMetaData : ImageMetaData
    {
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class Cfp2Format : ImageFormat
    {
        public override string         Tag { get { return "CFP/REB2"; } }
        public override string Description { get { return "Tail transparent bitmap"; } }
        public override uint     Signature { get { return 0x32424552; } } // 'REB2'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            int start_pos = header.ToInt32 (0x10);
            int extra_length = header.ToInt32 (0x1C);
            if (start_pos < 0x36)
                return null;
            return new CfpMetaData {
                Width = header.ToUInt32 (0x14),
                Height = header.ToUInt32 (0x18),
                BPP = 32,
                DataOffset = 0x20 + extra_length,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (CfpMetaData)info;
            int width = (int)meta.Width;
            int height = (int)meta.Height;
            int stride = width * 4;
            int total = width * height;
            int packed_size = total * 4;
            file.Position = meta.DataOffset;
            var pixels = new byte[stride*height];
            var input = file.ReadBytes (packed_size);
            int b = 0;
            int g = total;
            int r = total * 2;
            int a = total * 3;
            for (int dst_row = stride * (height - 1); dst_row >= 0; dst_row -= stride)
            {
                int dst = dst_row;
                for (int x = 0; x < width; ++x)
                {
                    pixels[dst++] = input[b++];
                    pixels[dst++] = input[g++];
                    pixels[dst++] = input[r++];
                    pixels[dst++] = input[a++];
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Cfp2Format.Write not implemented");
        }
    }
}
