//! \file       ImageHZC.cs
//! \date       Tue Dec 08 22:54:11 2015
//! \brief      Favorite View Point image format.
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
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FVP
{
    internal class HzcMetaData : ImageMetaData
    {
        public int  Type;
        public int  UnpackedSize;
        public int  HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class HzcFormat : ImageFormat
    {
        public override string         Tag { get { return "HZC"; } }
        public override string Description { get { return "Favorite View Point image format"; } }
        public override uint     Signature { get { return 0x31637A68; } } // 'HZC1'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x2C);
            if (!header.AsciiEqual (0xC, "NVSG"))
                return null;
            int type = header.ToUInt16 (0x12);
            return new HzcMetaData
            {
                Width   = header.ToUInt16 (0x14),
                Height  = header.ToUInt16 (0x16),
                OffsetX = header.ToInt16 (0x18),
                OffsetY = header.ToInt16 (0x1A),
                BPP     = 0 == type ? 24 : type > 2 ? 8 : 32,
                Type    = type,
                UnpackedSize = header.ToInt32 (4),
                HeaderSize   = header.ToInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (HzcMetaData)info;
            BitmapPalette palette = null;
            int stride = (int)meta.Width * meta.BPP / 8;
            PixelFormat format;
            switch (meta.Type)
            {
            default: throw new NotSupportedException();
            case 0: format = PixelFormats.Bgr24; break;
            case 1:
            case 2: format = PixelFormats.Bgra32; break;
            case 3: format = PixelFormats.Gray8; break;
            case 4:
                {
                    format = PixelFormats.Indexed8;
                    var colors = new Color[2] { Color.FromRgb (0,0,0), Color.FromRgb (0xFF,0xFF,0xFF) };
                    palette = new BitmapPalette (colors);
                    break;
                }
            }
            stream.Position = 12 + meta.HeaderSize;
            using (var z = new ZLibStream (stream.AsStream, CompressionMode.Decompress, true))
            {
                var pixels = new byte[stride * (int)meta.Height];
                if (pixels.Length != z.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                return ImageData.Create (info, format, palette, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HzcFormat.Write not implemented");
        }
    }
}
