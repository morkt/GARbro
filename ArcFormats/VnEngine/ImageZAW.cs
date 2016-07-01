//! \file       ImageZAW.cs
//! \date       Fri Jul 01 18:22:28 2016
//! \brief      GEM/vnengine image format.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.VnEngine
{
    [Export(typeof(ImageFormat))]
    public class ZawFormat : ImageFormat
    {
        public override string         Tag { get { return "ZAW"; } }
        public override string Description { get { return "GEM/vnengine image format"; } }
        public override uint     Signature { get { return 0x57415A; } } // 'ZAW'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x40];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            uint crc = LittleEndian.ToUInt32 (header, 4);
            LittleEndian.Pack (0u, header, 4);
            if (crc != Crc32.Compute (header, 0, header.Length))
                return null;
            int bpp = 3 == header[12] ? 32
                    : 2 == header[12] ? 16
                    : 1 == header[12] ? 24 : 8;
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0x10),
                Height  = LittleEndian.ToUInt32 (header, 0x14),
                OffsetX = LittleEndian.ToInt32  (header, 0x18),
                OffsetY = LittleEndian.ToInt32  (header, 0x1C),
                BPP     = bpp,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            int pixel_size = info.BPP / 8;
            int stride = (int)info.Width * pixel_size;
            var pixels = new byte[stride * (int)info.Height];
            stream.Position = 0x40;
            using (var input = new ZLibStream (stream, CompressionMode.Decompress, true))
                input.Read (pixels, 0, pixels.Length);
            if (24 == info.BPP)
                return ImageData.Create (info, PixelFormats.Rgb24, null, pixels, stride);
            if (8 == info.BPP)
                return ImageData.Create (info, PixelFormats.Gray8, null, pixels, stride);
            if (16 == info.BPP)
                return GrayWithAlpha (info, pixels);
            for (int i = 0; i < pixels.Length; i += pixel_size)
            {
                byte t = pixels[i];
                pixels[i] = pixels[i+2];
                pixels[i+2] = t;
                pixels[i+3] = (byte)System.Math.Min (pixels[i+3] * 0xFF / 0x80, 0xFF);
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        ImageData GrayWithAlpha (ImageMetaData info, byte[] input)
        {
            int stride = (int)info.Width * 4;
            byte[] pixels = new byte[stride * info.Height];
            int src = 0;
            for (int dst = 0; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = pixels[dst+1] = pixels[dst+2] = input[src++];
                pixels[dst+3] = (byte)System.Math.Min (input[src++] * 0xFF / 0x80, 0xFF);
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ZawFormat.Write not implemented");
        }
    }
}
