//! \file       ImageMAG.cs
//! \date       Sat Nov 28 03:17:00 2015
//! \brief      BANANA Shu-Shu MAG images implementation.
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

using GameRes.Compression;
using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Banana
{
    internal class MagMetaData : ImageMetaData
    {
        public uint AlphaOffset;
        public int  BackWidth;
        public int  BackHeight;
    }

    [Export(typeof(ImageFormat))]
    public class MagFormat : ImageFormat
    {
        public override string         Tag { get { return "MAG"; } }
        public override string Description { get { return "BANANA Shu-Shu image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x24);
            if (0 != header.ToInt32 (0x10) || 0 != header.ToInt32 (0x14))
                return null;
            int left = header.ToInt32 (0);
            int top  = header.ToInt32 (4);
            int right = header.ToInt32 (8);
            int bottom = header.ToInt32 (0xC);
            int back_width = header.ToInt32 (0x18);
            int back_height = header.ToInt32 (0x1C);
            uint alpha_channel = header.ToUInt32 (0x20);
            int width = right - left;
            int height = bottom - top;
            if (left >= back_width || top >= back_height
                || width <= 0 || width > back_width || height <= 0 || height > back_height
                || back_width <= 0 || back_width > 0x2000 || back_height <= 0 || back_height > 0x2000)
                return null;
            return new MagMetaData
            {
                Width   = (uint)width,
                Height  = (uint)height,
                BPP     = alpha_channel != 0 ? 32 : 24,
                OffsetX = left,
                OffsetY = top,
                AlphaOffset = alpha_channel,
                BackWidth = back_width,
                BackHeight = back_height,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            int stride = (int)info.Width * 3;
            var pixels = new byte[stride * (int)info.Height];
            stream.Position = 0x24;
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                if (pixels.Length != lz.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
            }
            int src = 0;
            for (int i = 3; i < stride; ++i)
            {
                pixels[i] += pixels[src++];
            }
            src = 0;
            for (int i = stride; i < pixels.Length; ++i)
            {
                pixels[i] += pixels[src++];
            }
            var meta = (MagMetaData)info;
            if (0 == meta.AlphaOffset)
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);

            stream.Position = 0x24 + meta.AlphaOffset;
            var alpha = new byte[meta.BackWidth*meta.BackHeight];
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                if (alpha.Length != lz.Read (alpha, 0, alpha.Length))
                    throw new InvalidFormatException();
            }
            int img_stride = (int)info.Width*4;
            var img = new byte[img_stride * (int)info.Height];
            int dst = 0;
            int alpha_y = meta.BackHeight - (meta.OffsetY + (int)meta.Height);
            for (int y = (int)meta.Height - 1; y >= 0; --y)
            {
                src = stride * y;
                int src_alpha = meta.BackWidth * (alpha_y + y) + meta.OffsetX;
                for (int i = 0; i < img_stride; i += 4)
                {
                    img[dst++] = pixels[src++];
                    img[dst++] = pixels[src++];
                    img[dst++] = pixels[src++];
                    img[dst++] = alpha[src_alpha++];
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, img, img_stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MagFormat.Write not implemented");
        }
    }
}
