//! \file       Image.cs
//! \date       Tue Jul 01 11:29:52 2014
//! \brief      image class.
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes
{
    public class ImageMetaData
    {
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int BPP { get; set; }
    }

    public class ImageEntry : Entry
    {
        public override string Type { get { return "image"; } }
    }

    public class ImageData
    {
        private BitmapSource m_bitmap;

        public BitmapSource Bitmap { get { return m_bitmap; } }
        public uint Width { get { return (uint)m_bitmap.PixelWidth; } }
        public uint Height { get { return (uint)m_bitmap.PixelHeight; } }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int BPP { get { return m_bitmap.Format.BitsPerPixel; } }

        public static double DefaultDpiX { get; set; }
        public static double DefaultDpiY { get; set; }

        static ImageData ()
        {
            SetDefaultDpi (96, 96);
        }

        public static void SetDefaultDpi (double x, double y)
        {
            DefaultDpiX = x;
            DefaultDpiY = y;
        }

        public ImageData (BitmapSource data, ImageMetaData meta)
        {
            m_bitmap = data;
            OffsetX = meta.OffsetX;
            OffsetY = meta.OffsetY;
        }

        public ImageData (BitmapSource data, int x = 0, int y = 0)
        {
            m_bitmap = data;
            OffsetX = x;
            OffsetY = y;
        }

        public static ImageData Create (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                        byte[] pixel_data, int stride)
        {
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, DefaultDpiX, DefaultDpiY,
                                              format, palette, pixel_data, stride);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public static ImageData Create (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                        byte[] pixel_data)
        {
            return Create (info, format, palette, pixel_data, (int)info.Width*((format.BitsPerPixel+7)/8));
        }
    }

    public abstract class ImageFormat : IResource
    {
        public override string Type { get { return "image"; } }

        public abstract ImageMetaData ReadMetaData (Stream file);

        public abstract ImageData Read (Stream file, ImageMetaData info);
        public abstract void Write (Stream file, ImageData bitmap);

        public static ImageData Read (Stream file)
        {
            return Read (null, file);
        }

        public static ImageData Read (string filename, Stream file)
        {
            bool need_dispose = false;
            try
            {
                if (!file.CanSeek)
                {
                    var stream = new MemoryStream();
                    file.CopyTo (stream);
                    file = stream;
                    need_dispose = true;
                }
                var format = FindFormat (file, filename);
                if (null == format)
                    return null;
                file.Position = 0;
                return format.Item1.Read (file, format.Item2);
            }
            finally
            {
                if (need_dispose)
                    file.Dispose();
            }
        }

        public static System.Tuple<ImageFormat, ImageMetaData> FindFormat (Stream file, string filename = null)
        {
            uint signature = FormatCatalog.ReadSignature (file);
            Lazy<string> ext = null;
            if (!string.IsNullOrEmpty (filename))
                ext = new Lazy<string> (() => Path.GetExtension (filename).TrimStart ('.').ToLowerInvariant());
            for (;;)
            {
                var range = FormatCatalog.Instance.LookupSignature<ImageFormat> (signature);
                // check formats that match filename extension first
                if (ext != null && range.Skip(1).Any())
                    range = range.OrderByDescending (f => f.Extensions.Any (e => e == ext.Value));
                foreach (var impl in range)
                {
                    try
                    {
                        file.Position = 0;
                        ImageMetaData metadata = impl.ReadMetaData (file);
                        if (null != metadata)
                            return new System.Tuple<ImageFormat, ImageMetaData> (impl, metadata);
                    }
                    catch { }
                }
                if (0 == signature)
                    break;
                signature = 0;
            }
            return null;
        }

        public override Entry CreateEntry ()
        {
            return new ImageEntry();
        }

        public bool IsBuiltin
        {
            get { return this.GetType().Assembly == typeof(ImageFormat).Assembly; }
        }
    }
}
