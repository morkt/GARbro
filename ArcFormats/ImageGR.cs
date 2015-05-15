//! \file       ImageGR.cs
//! \date       Fri May 15 04:26:58 2015
//! \brief      EAGLS system compressed bitmap.
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
using System.Windows.Media;
using System.IO;
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

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var reader = new LzssReader (stream, (int)stream.Length, 0x26)) // BMP header
            {
                reader.Unpack();
                var bmp = reader.Data;
                if (bmp[0] != 'B' || bmp[1] != 'M')
                    return null;
                int file_size = LittleEndian.ToInt32 (bmp, 2);
                int width = LittleEndian.ToInt32 (bmp, 0x12);
                int height = LittleEndian.ToInt32 (bmp, 0x16);
                int bpp = LittleEndian.ToInt16 (bmp, 0x1c);
                int image_size = LittleEndian.ToInt32 (bmp, 0x22);
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
            var meta = info as GrMetaData;
            if (null == meta)
                throw new ArgumentException ("GrFormat.Read should be supplied with GrMetaData", "info");

            using (var reader = new LzssReader (stream, (int)stream.Length, meta.UnpackedSize+2))
            {
                reader.Unpack();
                if (32 != info.BPP)
                    using (var bmp = new MemoryStream (reader.Data))
                        return base.Read (bmp, info);
                int stride = (int)info.Width*4;
                var pixels = new byte[stride*info.Height];
                int dst = 0;
                int offset = 0x36;
                for (int src = stride*((int)info.Height-1); src >= 0; src -= stride)
                {
                    Buffer.BlockCopy (reader.Data, offset+src, pixels, dst, stride);
                    dst += stride;
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
