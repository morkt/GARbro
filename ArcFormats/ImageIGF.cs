//! \file       ImageIGF.cs
//! \date       Sun Jun 28 00:13:01 2015
//! \brief      Silky's compressed image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    internal class IgfMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public bool IsPacked;
    }

    [Export(typeof(ImageFormat))]
    public class IgfFormat : ImageFormat
    {
        public override string         Tag { get { return "IGF"; } }
        public override string Description { get { return "Silky's image format"; } }
        public override uint     Signature { get { return 0x5355455Au; } } // 'ZEUS'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x14];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            uint width  = LittleEndian.ToUInt32 (header, 4);
            uint height = LittleEndian.ToUInt32 (header, 8);
            int unpacked_size  = LittleEndian.ToInt32 (header, 0xC);
            int flags = LittleEndian.ToInt32 (header, 0x10);
            int bpp = flags & 0xff;
            if (0 == bpp)
                bpp = 32;
            return new IgfMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                UnpackedSize = unpacked_size,
                IsPacked = 0 != (flags & 0x80000000),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as IgfMetaData;
            if (null == meta)
                throw new ArgumentException ("IgfFormat.Read should be supplied with IgfMetaData", "info");

            int stride = (int)info.Width*info.BPP/8;
            stream.Position = 0x14;
            byte[] pixels;
            if (meta.IsPacked)
            {
                int in_size = (int)(stream.Length - 0x14);
                using (var lzss = new LzssReader (stream, in_size, meta.UnpackedSize))
                {
                    lzss.FrameFill = 0x20;
                    lzss.Unpack();
                    pixels = lzss.Data;
                }
            }
            else
            {
                pixels = new byte[info.Height*stride];
                if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
            }
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Gray8;
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height,
                ImageData.DefaultDpiX, ImageData.DefaultDpiY, format, null, pixels, stride);
            var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
            flipped.Freeze();
            return new ImageData (flipped, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("IgfFormat.Write not implemented");
        }
    }
}
