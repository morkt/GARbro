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
using System.Text;
using System.Windows;
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

        private static ImageFormat s_TlgFormat = null;

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x2C;
            PixelFormat format = PixelFormats.Bgra32;
            WriteableBitmap bitmap = null;
            if (0x64 == stream.PeekByte ())
            {
                // This is a diff image, try to read base image
                stream.Position += 1;
                var name = stream.ReadCString (Encoding.UTF8);
                if (!string.IsNullOrEmpty (name))
                {
                    var entry = VFS.FindFile (Path.ChangeExtension (name, "hzc"));
                    if (null != entry)
                    {
                        var decoder = VFS.OpenImage (entry);
                        var image = decoder.Image;
                        decoder.Dispose();
                        if (null == image)
                            throw new InvalidFormatException ("Failed to decode base image.");
                        var converted = image.Bitmap;
                        if (converted.Format != format)
                            converted = new FormatConvertedBitmap (converted, format, null, 0);
                        var stride = converted.PixelWidth * 4;
                        var size = stride * converted.PixelHeight;
                        var pixels = new byte[size];
                        converted.CopyPixels (pixels, stride, 0);
                        bitmap = new WriteableBitmap (converted.PixelWidth, converted.PixelHeight,
                            ImageData.DefaultDpiX, ImageData.DefaultDpiY, format, null);
                        var rect = new Int32Rect (0, 0, converted.PixelWidth, converted.PixelHeight);
                        bitmap.WritePixels (rect, pixels, stride, 0);
                    }
                }
            }
            if (stream.ReadBytes (3).AsciiEqual ("TLG"))
            {
                // Read the TLG image
                var tlg_stream = new BinaryStream (new StreamRegion (stream.AsStream, stream.Position-3, true), stream.Name);
                if (null == s_TlgFormat)
                    s_TlgFormat = FindByTag ("TLG");
                var tlg_info = s_TlgFormat.ReadMetaData (tlg_stream);
                var image = s_TlgFormat.Read (tlg_stream, tlg_info);
                // No base image
                if (null == bitmap)
                    return image;
                // Size should be the same as the base image
                if (image.Width != bitmap.PixelWidth || image.Height != bitmap.PixelHeight)
                    return image;
                // Blend two images
                var converted = image.Bitmap;
                if (converted.Format != format)
                    converted = new FormatConvertedBitmap (converted, format, null, 0);
                var rect = new Int32Rect (0, 0, converted.PixelWidth, converted.PixelHeight);
                BlendBitmap (converted, rect, bitmap, 0, 0);
                bitmap.Freeze ();
                return new ImageData (bitmap, tlg_info);
            }
            var meta = (HzcMetaData)info;
            stream.Position = 12 + meta.HeaderSize;
            using (var decoder = new HzcDecoder (stream, meta, true))
                return decoder.Image;
        }

        void BlendBitmap (BitmapSource bitmap, Int32Rect source, WriteableBitmap output, int x, int y)
        {
            int src_stride = source.Width * 4;
            var pixels = new byte[src_stride * source.Height];
            bitmap.CopyPixels (source, pixels, src_stride, 0);
            unsafe
            {
                int dst_stride = output.BackBufferStride;
                int offset = y * dst_stride + x * 4;
                byte* buffer = (byte*)(output.BackBuffer + offset);
                int src = 0;
                for (int h = 0; h < source.Height; ++h)
                {
                    int dst = 0;
                    for (int w = 0; w < source.Width; ++w)
                    {
                        byte src_alpha = pixels[src+3];
                        if (src_alpha > 0)
                        {
                            if (0xFF == src_alpha || 0 == buffer[dst+3])
                            {
                                buffer[dst  ] = pixels[src];
                                buffer[dst+1] = pixels[src+1];
                                buffer[dst+2] = pixels[src+2];
                            }
                            else
                            {
                                buffer[dst+0] = (byte)((pixels[src+0] * src_alpha + buffer[dst+0] * (0xFF - src_alpha)) / 0xFF);
                                buffer[dst+1] = (byte)((pixels[src+1] * src_alpha + buffer[dst+1] * (0xFF - src_alpha)) / 0xFF);
                                buffer[dst+2] = (byte)((pixels[src+2] * src_alpha + buffer[dst+2] * (0xFF - src_alpha)) / 0xFF);
                            }
                            buffer[dst+3] = src_alpha;
                        }
                        dst += 4;
                        src += 4;
                    }
                    buffer += dst_stride;
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HzcFormat.Write not implemented");
        }
    }

    internal sealed class HzcDecoder : IImageDecoder
    {
        HzcMetaData     m_info;
        ImageData       m_image;
        int             m_stride;
        long            m_frame_offset;
        int             m_frame_size;

        public Stream            Source { get; private set; }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public PixelFormat       Format { get; private set; }
        public BitmapPalette    Palette { get; private set; }
        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    var pixels = ReadPixels();
                    m_image = ImageData.Create (Info, Format, Palette, pixels, m_stride);
                }
                return m_image;
            }
        }

        public HzcDecoder (IBinaryStream input, HzcMetaData info, Entry entry) : this (input, info)
        {
            m_frame_offset = entry.Offset;
            m_frame_size = (int)entry.Size;
        }

        public HzcDecoder (IBinaryStream input, HzcMetaData info, bool leave_open = false)
        {
            m_info = info;
            m_stride = (int)m_info.Width * m_info.BPP / 8;
            switch (m_info.Type)
            {
            default: throw new NotSupportedException();
            case 0: Format = PixelFormats.Bgr24; break;
            case 1:
            case 2: Format = PixelFormats.Bgra32; break;
            case 3: Format = PixelFormats.Gray8; break;
            case 4:
                {
                    Format = PixelFormats.Indexed8;
                    var colors = new Color[2] { Color.FromRgb (0,0,0), Color.FromRgb (0xFF,0xFF,0xFF) };
                    Palette = new BitmapPalette (colors);
                    break;
                }
            }
            Source = new ZLibStream (input.AsStream, CompressionMode.Decompress, leave_open);
            m_frame_offset = 0;
            m_frame_size = m_stride * (int)Info.Height;
        }

        byte[] ReadPixels ()
        {
            var pixels = new byte[m_frame_size];
            long offset = 0;
            for (;;)
            {
                if (pixels.Length != Source.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                if (offset >= m_frame_offset)
                    break;
                offset += m_frame_size;
            }
            return pixels;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                Source.Dispose();
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
    }
}
