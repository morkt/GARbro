//! \file       ImageGDT.cs
//! \date       2018 Jan 04
//! \brief      Pochette bitmap container.
//
// Copyright (C) 2018 by morkt
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Pochette
{
    internal class GdtMetaData : ImageMetaData
    {
        public uint     BitmapOffset;
        public string   BaseLine;
    }

    [Export(typeof(ImageFormat))]
    public class GdtFormat : ImageFormat
    {
        public override string         Tag { get { return "GDT"; } }
        public override string Description { get { return "Pochette bitmap container"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (16);
            int base_length = header[8];
            if (base_length != 7 && base_length != 0)
                return null;
            var info = Bmp.ReadMetaData (file);
            if (null == info)
                return null;
            string base_name = null;
            if (base_length != 0)
            {
                base_name = header.GetCString (9, base_length);
                if (string.IsNullOrWhiteSpace (base_name))
                    base_name = null;
            }
            return new GdtMetaData {
                Width = info.Width,
                Height = info.Height,
                OffsetX = header.ToInt16 (0),
                OffsetY = header.ToInt16 (2),
                BPP = info.BPP,
                BitmapOffset = 16,
                BaseLine = base_name,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GdtMetaData)info;
            var bitmap = ReadBitmapSource (file, meta);
            if (null != meta.BaseLine)
                bitmap = BlendBaseLine (bitmap, meta);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        BitmapSource ReadBitmapSource (IBinaryStream file, GdtMetaData info)
        {
            using (var bmp = new StreamRegion (file.AsStream, info.BitmapOffset, true))
            {
                var decoder = new BmpBitmapDecoder (bmp,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }

        BitmapSource BlendBaseLine (BitmapSource overlay, GdtMetaData meta)
        {
            string base_name = VFS.ChangeFileName (meta.FileName, meta.BaseLine);
            if (!VFS.FileExists (base_name))
            {
                base_name += ".gdt";
                if (!VFS.FileExists (base_name))
                    return overlay;
            }
            using (var base_file = VFS.OpenBinaryStream (base_name))
            {
                var base_info = ReadMetaData (base_file) as GdtMetaData;
                if (null == base_info)
                    return overlay;
                var base_image = ReadBitmapSource (base_file, base_info);
                if (base_image.Format.BitsPerPixel < 24)
                    base_image = new FormatConvertedBitmap (base_image, PixelFormats.Bgr32, null, 0);
                var canvas = new WriteableBitmap (base_image);
                int canvas_bpp = canvas.Format.BitsPerPixel;
                if (canvas_bpp != overlay.Format.BitsPerPixel)
                    overlay = new FormatConvertedBitmap (overlay, canvas.Format, null, 0);
                canvas.Lock();
                unsafe
                {
                    byte* buffer = (byte*)canvas.BackBuffer;
                    int canvas_stride = canvas.BackBufferStride;
                    int canvas_size = canvas_stride * canvas.PixelHeight;
                    int overlay_stride = (overlay.PixelWidth * canvas_bpp + 7) / 8;
                    int overlay_size = overlay_stride * overlay.PixelHeight;
                    int pos = meta.OffsetY * canvas_stride + meta.OffsetX * canvas_bpp / 8;
                    overlay.CopyPixels (Int32Rect.Empty, (IntPtr)(buffer+pos), canvas_size-pos, canvas_stride);
                }
                var rect = new Int32Rect (meta.OffsetX, meta.OffsetY, overlay.PixelWidth, overlay.PixelHeight);
                canvas.AddDirtyRect (rect);
                canvas.Unlock();
                return canvas;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GdtFormat.Write not implemented");
        }
    }
}
