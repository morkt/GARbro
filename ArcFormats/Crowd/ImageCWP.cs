//! \file       ImageCWP.cs
//! \date       Thu Jun 11 13:43:41 2015
//! \brief      Crowd engine image format.
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

        public override ImageMetaData ReadMetaData (IBinaryStream input)
        {
            input.Position = 4;
            uint width  = Binary.BigEndian (input.ReadUInt32());
            uint height = Binary.BigEndian (input.ReadUInt32());
            if (0 == width || 0 == height)
                return null;
            int bpp = input.ReadByte();
            int color_type = input.ReadByte();
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
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var header = new byte[0x15];
            using (var mem = new MemoryStream((int)(0x14 + file.Length + 12)))
            using (var png = new BinaryWriter (mem))
            {
                png.Write (0x474E5089u); // png header
                png.Write (0x0A1A0A0Du);
                png.Write (0x0D000000u);
                png.Write (0x52444849u); // 'IHDR'
                file.Position = 4;
                file.Read (header, 0, header.Length);
                png.Write (header, 0, header.Length);
                png.Write (0x54414449u); // 'IDAT'
                file.AsStream.CopyTo (mem);
                header[1] = 0;
                header[2] = 0;
                header[3] = 0;
                LittleEndian.Pack (0x444E4549, header, 4);
                LittleEndian.Pack (0x826042AE, header, 8);
                png.Write (header, 1, 11);
                mem.Position = 0;
                var decoder = new PngBitmapDecoder (mem, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                var pixels = new byte[info.Width*info.Height*4];
                frame.CopyPixels (pixels, (int)info.Width*4, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte t = pixels[i];
                    pixels[i] = pixels[i+2];
                    pixels[i+2] = t;
                }
                return ImageData.Create (info, PixelFormats.Bgr32, null, pixels);
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
