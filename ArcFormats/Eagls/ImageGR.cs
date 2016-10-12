//! \file       ImageGR.cs
//! \date       Fri May 15 04:26:58 2015
//! \brief      EAGLS system compressed bitmap.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Windows.Media;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Eagls
{
    internal class GrMetaData : ImageMetaData
    {
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrFormat : BmpFormat
    {
        public override string         Tag { get { return "GR"; } }
        public override string Description { get { return "EAGLS engine compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var lzs = new LzssStream (stream, LzssMode.Decompress, true))
            {
                if (lzs.ReadByte() != 'B' || lzs.ReadByte() != 'M')
                    return null;
                var bmp = new byte[0x26];
                if (0x24 != lzs.Read (bmp, 2, 0x24))
                    return null;
                int file_size   = LittleEndian.ToInt32 (bmp, 2);
                int width       = LittleEndian.ToInt32 (bmp, 0x12);
                int height      = LittleEndian.ToInt32 (bmp, 0x16);
                int bpp         = LittleEndian.ToInt16 (bmp, 0x1C);
                int image_size  = LittleEndian.ToInt32 (bmp, 0x22);
                if (0 == image_size)
                    image_size = width * height * (bpp / 8);
                return new GrMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = bpp,
                    UnpackedSize = 24 == bpp ? file_size : (image_size+0x36),
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (GrMetaData)info;
            using (var bmp = new LzssStream (stream, LzssMode.Decompress, true))
            {
                if (32 != info.BPP)
                    return base.Read (bmp, info);
                int stride = (int)info.Width*4;
                var pixels = new byte[Math.Max (0x36, stride*info.Height)];
                bmp.Read (pixels, 0, 0x36); // skip header
                for (int y = (int)info.Height - 1; y >= 0; --y)
                {
                    int dst = y * stride;
                    bmp.Read (pixels, dst, stride);
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrFormat.Write not implemented");
        }
    }
}
