//! \file       ImageCWP.cs
//! \date       Thu Jun 11 13:43:41 2015
//! \brief      Crowd engine image format.
//
// Copyright (C) 2015-2018 by morkt
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
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ImageFormat))]
    public class CwpFormat : ImageFormat
    {
        public override string         Tag { get { return "CWP"; } }
        public override string Description { get { return "Crowd engine image format"; } }
        public override uint     Signature { get { return 0x50445743; } } // 'CWDP'
        public override bool      CanWrite { get { return true; } }

        public CwpFormat ()
        {
            Extensions = new string[] { "cwp", "amp" };
            Signatures = new uint[] { 0x50445743, 0x504E4D41 }; // 'AMNP'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream input)
        {
            var header = input.ReadHeader (0x11);
            uint width  = BigEndian.ToUInt32 (header, 4);
            uint height = BigEndian.ToUInt32 (header, 8);
            if (0 == width || 0 == height)
                return null;
            int bpp = header[0xC];
            if (bpp != 1 && bpp != 2 && bpp != 4 && bpp != 8 && bpp != 16)
                return null;
            int color_type = header[0xD];
            switch (color_type)
            {
            case 2: bpp *= 3; break;
            case 4: bpp *= 2; break;
            case 6: bpp *= 4; break;
            case 3:
            case 0: break;
            default: return null;
            }
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 32, // always converted to 32bpp
            };
        }

        Stream OpenAsPng (IBinaryStream input)
        {
            var header = new byte[0x29];
            Buffer.BlockCopy (PngFormat.HeaderBytes, 0, header, 0, PngFormat.HeaderBytes.Length);
            header[0xB] = 0xD;
            Encoding.ASCII.GetBytes ("IHDR", 0, 4, header, 0xC);
            input.Position = 4;
            input.Read (header, 0x10, 0x15);
            Encoding.ASCII.GetBytes ("IDAT", 0, 4, header, 0x25);
            var footer = new byte[] {
                0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82
            };
            Stream png = new StreamRegion (input.AsStream, 0x19, true);
            Stream end = new MemoryStream (footer);
            png = new ConcatStream (png, end);
            return new PrefixStream (header, png);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var png = OpenAsPng (file))
            {
                var decoder = new PngBitmapDecoder (png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                if (frame.Format.BitsPerPixel != 32)
                    frame = new FormatConvertedBitmap (frame, PixelFormats.Bgr32, null, 0);
                int stride = (int)info.Width * 4;
                var pixels = new byte[(int)info.Height * stride];
                frame.CopyPixels (pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte t = pixels[i];
                    pixels[i] = pixels[i+2];
                    pixels[i+2] = t;
                }
                return ImageData.Create (info, PixelFormats.Bgr32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            var writer = new Writer (image);
            writer.Write (file);
        }

        internal class Writer
        {
            BitmapSource    m_bitmap;
            byte[]          m_buffer = new byte[81920];

            public Writer (ImageData image)
            {
                m_bitmap = image.Bitmap;
                if (m_bitmap.Format.BitsPerPixel < 32)
                {
                    m_bitmap = new FormatConvertedBitmap (m_bitmap, PixelFormats.Bgr32, null, 0);
                }
                int stride = m_bitmap.PixelWidth * 4;
                var pixels = new byte[stride*m_bitmap.PixelHeight];
                m_bitmap.CopyPixels (pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte t = pixels[i];
                    pixels[i] = pixels[i+2];
                    pixels[i+2] = t;
                }
                m_bitmap = BitmapSource.Create (m_bitmap.PixelWidth, m_bitmap.PixelHeight,
                    m_bitmap.DpiX, m_bitmap.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            }

            public void Write (Stream file)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add (BitmapFrame.Create (m_bitmap));
                using (var png = new MemoryStream())
                using (var cwp = new BinaryWriter (file, System.Text.Encoding.ASCII, true))
                {
                    encoder.Save (png);
                    var header = new byte[0x11];
                    png.Position = 0x10;
                    png.Read (header, 0, header.Length);
                    cwp.Write (0x50445743u); // 'CWDP'
                    cwp.Write (header, 0, header.Length);
                    long idat;
                    using (var bin = new BinMemoryStream (png, ""))
                        idat = PngFormat.FindChunk (bin, "IDAT");
                    if (-1 == idat)
                        throw new InvalidFormatException ("CWP conversion failed");
                    png.Position = idat;
                    png.Read (header, 0, 8);
                    int chunk_size = BigEndian.ToInt32 (header, 0) + 4;
                    cwp.Write (header, 0, 4);
                    for (;;)
                    {
                        CopyChunk (png, file, chunk_size);
                        if (8 != png.Read (header, 0, 8))
                            throw new InvalidFormatException ("CWP conversion failed");
                        if (Binary.AsciiEqual (header, 4, "IEND"))
                        {
                            cwp.Write ((byte)0);
                            break;
                        }
                        chunk_size = BigEndian.ToInt32 (header, 0) + 4;
                        cwp.Write (header, 0, 8);
                    }
                }
            }

            private void CopyChunk (Stream src, Stream dst, int size)
            {
                while (size > 0)
                {
                    int amount = Math.Min (size, m_buffer.Length);
                    int read = src.Read (m_buffer, 0, amount);
                    if (read != amount)
                        throw new InvalidFormatException ("CWP conversion failed");
                    dst.Write (m_buffer, 0, amount);
                    size -= amount;
                }
            }
        }
    }
}
