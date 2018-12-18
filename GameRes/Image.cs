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
        /// <summary>Image width in pixels.</summary>
        public uint Width { get; set; }

        /// <summary>Image height in pixels.</summary>
        public uint Height { get; set; }

        /// <summary>Horizontal coordinate of the image top left corner.</summary>
        public int OffsetX { get; set; }

        /// <summary>Vertical coordinate of the image top left corner.</summary>
        public int OffsetY { get; set; }

        /// <summary>Image bitdepth.</summary>
        public int BPP { get; set; }

        /// <summary>Image source file name, if any.</summary>
        public string FileName { get; set; }

        public int iWidth  { get { return (int)Width; } }
        public int iHeight { get { return (int)Height; } }
    }

    public class ImageEntry : Entry
    {
        public override string Type { get { return "image"; } }
    }

    /// <summary>
    /// Enumeration representing possible palette serialization formats.
    /// </summary>
    public enum PaletteFormat
    {
        Rgb     = 1,
        Bgr     = 2,
        RgbX    = 5,
        BgrX    = 6,
        RgbA    = 9,
        BgrA    = 10,
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
                                        Array pixel_data, int stride)
        {
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, DefaultDpiX, DefaultDpiY,
                                              format, palette, pixel_data, stride);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public static ImageData Create (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                        Array pixel_data)
        {
            return Create (info, format, palette, pixel_data, (int)info.Width*((format.BitsPerPixel+7)/8));
        }

        public static ImageData CreateFlipped (ImageMetaData info, PixelFormat format, BitmapPalette palette,
                                               Array pixel_data, int stride)
        {
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, DefaultDpiX, DefaultDpiY,
                                              format, palette, pixel_data, stride);
            var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
            flipped.Freeze();
            return new ImageData (flipped, info);
        }
    }

    public abstract class ImageFormat : IResource
    {
        public override string Type { get { return "image"; } }

        public abstract ImageMetaData ReadMetaData (IBinaryStream file);

        public abstract ImageData Read (IBinaryStream file, ImageMetaData info);
        public abstract void Write (Stream file, ImageData bitmap);

        public static ImageData Read (IBinaryStream file)
        {
            var format = FindFormat (file);
            if (null == format)
                return null;
            file.Position = 0;
            return format.Item1.Read (file, format.Item2);
        }

        public static System.Tuple<ImageFormat, ImageMetaData> FindFormat (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<ImageFormat> (file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    ImageMetaData metadata = impl.ReadMetaData (file);
                    if (null != metadata)
                    {
                        metadata.FileName = file.Name;
                        return Tuple.Create (impl, metadata);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        public bool IsBuiltin
        {
            get { return this.GetType().Assembly == typeof(ImageFormat).Assembly; }
        }

        public static ImageFormat FindByTag (string tag)
        {
            return FormatCatalog.Instance.ImageFormats.FirstOrDefault (x => x.Tag == tag);
        }

        static readonly Lazy<ImageFormat> s_JpegFormat = new Lazy<ImageFormat> (() => FindByTag ("JPEG"));
        static readonly Lazy<ImageFormat> s_PngFormat  = new Lazy<ImageFormat> (() => FindByTag ("PNG"));
        static readonly Lazy<ImageFormat> s_BmpFormat  = new Lazy<ImageFormat> (() => FindByTag ("BMP"));
        static readonly Lazy<ImageFormat> s_TgaFormat  = new Lazy<ImageFormat> (() => FindByTag ("TGA"));

        public static ImageFormat Jpeg { get { return s_JpegFormat.Value; } }
        public static ImageFormat  Png { get { return s_PngFormat.Value; } }
        public static ImageFormat  Bmp { get { return s_BmpFormat.Value; } }
        public static ImageFormat  Tga { get { return s_TgaFormat.Value; } }

        /// <summary>
        /// Desereialize color map from <paramref name="input"/> stream, consisting of specified number of
        /// <paramref name="colors"/> stored in specified <paramref name="format"/>.
        /// Default number of colors is 256 and format is 4-byte BGRX (where X is an unsignificant byte).
        /// </summary>
        public static Color[] ReadColorMap (Stream input, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            int bpp = PaletteFormat.Rgb == format || PaletteFormat.Bgr == format ? 3 : 4;
            var palette_data = new byte[bpp * colors];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            int src = 0;
            var color_map = new Color[colors];
            Func<int, Color> get_color;
            if (PaletteFormat.Bgr == format || PaletteFormat.BgrX == format)
                get_color = x => Color.FromRgb (palette_data[x+2], palette_data[x+1], palette_data[x]);
            else if (PaletteFormat.BgrA == format)
                get_color = x => Color.FromArgb (palette_data[x+3], palette_data[x+2], palette_data[x+1], palette_data[x]);
            else if (PaletteFormat.RgbA == format)
                get_color = x => Color.FromArgb (palette_data[x+3], palette_data[x], palette_data[x+1], palette_data[x+2]);
            else
                get_color = x => Color.FromRgb (palette_data[x],   palette_data[x+1], palette_data[x+2]);

            for (int i = 0; i < colors; ++i)
            {
                color_map[i] = get_color (src);
                src += bpp;
            }
            return color_map;
        }

        public static BitmapPalette ReadPalette (Stream input, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            return new BitmapPalette (ReadColorMap (input, colors, format));
        }

        public static BitmapPalette ReadPalette (ArcView file, long offset, int colors = 0x100, PaletteFormat format = PaletteFormat.BgrX)
        {
            using (var input = file.CreateStream (offset, (uint)(4 * colors))) // largest possible size for palette
                return ReadPalette (input, colors, format);
        }
    }
}
