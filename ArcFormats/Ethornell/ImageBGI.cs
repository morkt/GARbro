//! \file       ImageBGI.cs
//! \date       Fri Apr 03 01:39:41 2015
//! \brief      BGI/Ethornell engine image format.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.BGI
{
    internal class BgiMetaData : ImageMetaData
    {
        public bool IsScrambled;
    }

    [Export(typeof(ImageFormat))]
    public class BgiFormat : ImageFormat
    {
        public override string         Tag { get { return "BGI"; } }
        public override string Description { get { return "BGI/Ethornell image format"; } }
        public override uint     Signature { get { return 0; } }

        public BgiFormat ()
        {
            Extensions = new string[] { "", "bgi", "_bg" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int width  = stream.ReadInt16();
            int height = stream.ReadInt16();
            if (width <= 0 || height <= 0)
                return null;
            int bpp = stream.ReadInt16();
            if (24 != bpp && 32 != bpp && 8 != bpp)
                return null;
            int flag = stream.ReadInt16();
            if (flag != 1 && flag != 0)
                return null;
            if (0 != stream.ReadInt64())
                return null;
            return new BgiMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = bpp,
                IsScrambled = flag != 0,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (BgiMetaData)info;
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Gray8;
            int stride = (int)info.Width * ((info.BPP+7)/8);
            var pixels = new byte[stride * (int)info.Height];
            stream.Position = 0x10;
            if (!meta.IsScrambled)
            {
                int read = stream.Read (pixels, 0, pixels.Length);
                if (read != pixels.Length)
                    throw new InvalidFormatException();
            }
            else
            {
                RestorePixels (stream, pixels, meta);
            }
            return ImageData.Create (info, format, null, pixels, stride);
        }

        void RestorePixels (IBinaryStream input, byte[] output, BgiMetaData info)
        {
            int bpp = info.BPP / 8;
            int stride = (int)info.Width * bpp;
            for (int i = 0; i < bpp; ++i)
            {
                int dst = i;
                byte incr = 0;
                for (int h = (int)info.Height; h > 0; --h)
                {
                    for (uint w = 0; w < info.Width; ++w)
                    {
                        incr += input.ReadUInt8();
                        output[dst] = incr;
                        dst += bpp;
                    }
                    if (--h == 0)
                        break;
                    dst += stride;
                    int pos = dst;
                    for (uint w = 0; w < info.Width; ++w)
                    {
                        pos -= bpp;
                        incr += input.ReadUInt8();
                        output[pos] = incr;
                    }
                }
            }
        }
    }
}
