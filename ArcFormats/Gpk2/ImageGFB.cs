//! \file       ImageGFB.cs
//! \date       Sun Oct 25 13:06:47 2015
//! \brief      GPK2 image resource.
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

namespace GameRes.Formats.Gpk2
{
    internal class GfbMetaData : ImageMetaData
    {
        public int  PackedSize;
        public int  UnpackedSize;
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GfbFormat : ImageFormat
    {
        public override string         Tag { get { return "GFB"; } }
        public override string Description { get { return "GPK2 image format"; } }
        public override uint     Signature { get { return 0x20424647; } } // 'GFB '

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            return new GfbMetaData
            {
                Width   = header.ToUInt32 (0x1C),
                Height  = header.ToUInt32 (0x20),
                BPP     = header.ToUInt16 (0x26),
                PackedSize = header.ToInt32 (0x0C),
                UnpackedSize = header.ToInt32 (0x10),
                DataOffset = header.ToInt32 (0x14),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GfbMetaData)info;
            BitmapPalette palette = null;
            if (8 == meta.BPP && meta.DataOffset != 0x40)
            {
                stream.Position = 0x40;
                palette = ReadPalette (stream.AsStream, meta.DataOffset - 0x40);
            }

            stream.Position = meta.DataOffset;
            byte[] pixels = new byte[meta.UnpackedSize];
            if (0 != meta.PackedSize)
            {
                using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
                    lzss.Read (pixels, 0, pixels.Length);
            }
            else
                stream.Read (pixels, 0, pixels.Length);

            PixelFormat format;
            switch (meta.BPP)
            {
            case 32:
                if (HasAlphaChannel (pixels))
                    format = PixelFormats.Bgra32;
                else
                    format = PixelFormats.Bgr32;
                break;

            case 24:
                format = PixelFormats.Bgr24;
                break;

            case 16:
                format = PixelFormats.Bgr565;
                break;

            case 8:
                if (null != palette)
                    format = PixelFormats.Indexed8;
                else
                    format = PixelFormats.Gray8;
                break;

            default:
                throw new NotSupportedException ("Not supported GFB color depth");
            }
            int stride = pixels.Length / (int)info.Height;

            return ImageData.CreateFlipped (info, format, palette, pixels, stride);
        }

        BitmapPalette ReadPalette (Stream input, int palette_size)
        {
            palette_size = Math.Min (0x400, palette_size);
            var palette_data = new byte[palette_size];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            int color_size = palette_size / 0x100;
            var palette = new Color[0x100];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * color_size;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return new BitmapPalette (palette);
        }

        static bool HasAlphaChannel (byte[] pixels)
        {
            for (int p = 3; p < pixels.Length; p += 4)
            {
                if (pixels[p] > 0)
                    return true;
            }
            return false;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GfbFormat.Write not implemented");
        }
    }
}
