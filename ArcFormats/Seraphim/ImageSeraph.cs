//! \file       ImageSeraph.cs
//! \date       Sat Jul 18 12:16:42 2015
//! \brief      Seraphim engine images.
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

namespace GameRes.Formats.Seraphim
{
    internal class SeraphMetaData : ImageMetaData
    {
        public int PackedSize;
        public int Colors;
    }

    [Export(typeof(ImageFormat))]
    public class SeraphCfImage : ImageFormat
    {
        public override string         Tag { get { return "CF"; } }
        public override string Description { get { return "Seraphim engine image format"; } }
        public override uint     Signature { get { return 0x4643; } }

        public SeraphCfImage ()
        {
            Extensions = new string[] { "cts" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            int packed_size = header.ToInt32 (12);
            if (packed_size <= 0 || packed_size > stream.Length-0x10)
                return null;
            uint width  = header.ToUInt16 (8);
            uint height = header.ToUInt16 (10);
            if (0 == width || 0 == height)
                return null;
            return new SeraphMetaData
            {
                OffsetX = header.ToInt16 (4),
                OffsetY = header.ToInt16 (6),
                Width   = width,
                Height  = height,
                BPP     = 24,
                PackedSize = packed_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SeraphMetaData)info;
            var reader = new SeraphReader (stream.AsStream, meta);
            reader.UnpackCf();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("SeraphCfImage.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class SeraphCtImage : SeraphCfImage
    {
        public override string         Tag { get { return "CT"; } }
        public override uint     Signature { get { return 0x5443; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var info = base.ReadMetaData (stream);
            if (info != null)
                info.BPP = 32;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SeraphMetaData)info;
            var reader = new SeraphReader (stream.AsStream, meta);
            reader.UnpackCt();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("SeraphCtImage.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class SeraphCbImage : ImageFormat
    {
        public override string         Tag { get { return "CB"; } }
        public override string Description { get { return "Seraphim engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            if ('C' != header[0] || 'B' != header[1])
                return null;
            int colors = header.ToUInt16 (2);
            int packed_size = header.ToInt32 (12);
            if (packed_size <= 0 || packed_size > stream.Length-0x10)
                return null;
            uint width  = header.ToUInt16 (8);
            uint height = header.ToUInt16 (10);
            if (0 == width || 0 == height)
                return null;
            return new SeraphMetaData
            {
                OffsetX = header.ToInt16 (4),
                OffsetY = header.ToInt16 (6),
                Width   = width,
                Height  = height,
                BPP     = 8,
                PackedSize = packed_size,
                Colors  = colors,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SeraphMetaData)info;
            var reader = new SeraphReader (stream.AsStream, meta, 1);
            reader.UnpackCb();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("SeraphCbImage.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class SeraphCxImage : SeraphCfImage
    {
        public override string         Tag { get { return "CX"; } }
        public override uint     Signature { get { return 0x5843; } } // 'CX'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var info = base.ReadMetaData (stream);
            if (info != null)
                info.BPP = 32;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new SeraphReader (stream.AsStream, (SeraphMetaData)info, 4);
            reader.UnpackCx();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("SeraphCxImage.Write not implemented");
        }
    }

    internal class SeraphReader
    {
        Stream      m_input;
        byte[]      m_output;
        int         m_width;
        int         m_height;
        int         m_stride;
        int         m_colors;
        int         m_packed_size;
        int         m_pixel_size;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public ImageMetaData    Info { get; private set; }

        public SeraphReader (Stream input, SeraphMetaData info, int pixel_size = 3)
        {
            Info = info;
            m_input = input;
            m_input.Position = 0x10;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_stride = m_width * pixel_size;
            m_output = new byte[m_stride * m_height];
            m_packed_size = info.PackedSize;
            m_colors = info.Colors;
            m_pixel_size = pixel_size;
            if (1 == pixel_size && m_colors > 0)
                Palette = ReadPalette (m_colors);
        }

        public BitmapPalette ReadPalette (int colors)
        {
            return ImageFormat.ReadPalette (m_input, Math.Min (colors, 0x100), PaletteFormat.Rgb);
        }

        public void UnpackCb ()
        {
            var pixels = UnpackBytes();
            int dst = 0;
            for (int src = (m_height-1) * m_width; src >= 0; src -= m_width)
            {
                Buffer.BlockCopy (pixels, src, m_output, dst, m_width);
                dst += m_width;
            }
            Format = PixelFormats.Indexed8;
        }

        public void UnpackCt ()
        {
            UnpackRgb();
            m_input.Position = 0x10 + m_packed_size + 4;
            var alpha = UnpackBytes();
            var pixels = new byte[m_width*m_height*4];
            int dst = 0;
            for (int y = m_height-1; y >= 0; --y)
            {
                int rgb = y * m_stride;
                int a   = y * m_width;
                for (int x = 0; x < m_width; ++x)
                {
                    pixels[dst++] = m_output[rgb++];
                    pixels[dst++] = m_output[rgb++];
                    pixels[dst++] = m_output[rgb++];
                    int v = Math.Min (alpha[a++] * 0xff / 0x64, 0xff);
                    pixels[dst++] = (byte)~v;
                }
            }
            m_output = pixels;
            Format = PixelFormats.Bgra32;
        }

        public void UnpackCf ()
        {
            UnpackRgb();
            FlipPixels();
            Format = PixelFormats.Bgr24;
        }

        public void UnpackCx ()
        {
            UnpackRgb();
            FlipPixels();
            Format = PixelFormats.Bgra32;
        }

        private void UnpackRgb () // sub_404250
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count;
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                if ((ctl & 0xF0) == 0xF0)
                    throw new InvalidFormatException();

                if (0 == (ctl & 0x80))
                {
                    if (0 != (ctl & 0x40))
                    {
                        count = (ctl & 0x3F) + 2;
                        FillBytes (dst, (byte)m_input.ReadByte(), count);
                    }
                    else
                    {
                        count = (ctl & 0x3F) + 1;
                        if (count != m_input.Read (m_output, dst, count))
                            break;
                    }
                }
                else if (0 == (ctl & 0x40))
                {
                    count = m_input.ReadByte() | ((ctl & 0xF) << 8);
                    switch ((ctl >> 4) & 3)
                    {
                    case 0:
                        count += 2;
                        FillBytes (dst, (byte)m_input.ReadByte(), count);
                        break;
                    case 1:
                        ++count;
                        Binary.CopyOverlapped (m_output, dst-m_stride, dst, count);
                        break;
                    case 2:
                        ++count;
                        Binary.CopyOverlapped (m_output, dst-2*m_stride, dst, count);
                        break;
                    case 3:
                        ++count;
                        Binary.CopyOverlapped (m_output, dst-4*m_stride, dst, count);
                        break;
                    }
                }
                else if (0 == (ctl & 0x30))
                {
                    count = m_input.ReadByte() + ((ctl & 7) << 8) + 1;
                    int x = m_pixel_size;
                    if (0 != (ctl & 8))
                        x *= 2;
                    m_input.Read (m_output, dst, x);
                    Binary.CopyOverlapped (m_output, dst, dst+x, count*x);
                    ++count;
                    count *= x;
                }
                else if (0 == (ctl & 0x20))
                {
                    int offset = m_input.ReadByte() + ((ctl & 0xF) << 8) + 1;
                    count = m_input.ReadByte() + 1;
                    int src = dst - m_pixel_size * offset;
                    count *= m_pixel_size;
                    Binary.CopyOverlapped (m_output, src, dst, count);
                }
                else
                {
                    int offset = m_input.ReadByte() + ((ctl & 0xF) << 8) + 1;
                    count = m_input.ReadByte() + 1;
                    int src = dst - offset;
                    Binary.CopyOverlapped (m_output, src, dst, count);
                }
                if (0 == count)
                    throw new InvalidFormatException();
                dst += count;
            }
        }

        private byte[] UnpackBytes () // sub_403ED0
        {
            int total = m_width * m_height;
            var output = new byte[total];
            int dst = 0;
            while ( dst < total )
            {
                int count;
                int next = m_input.ReadByte();
                if (-1 == next)
                    break;
                if ((next & 0xF0) == 0xF0)
                    throw new InvalidFormatException();

                if (0 == (next & 0x80))
                {
                    if (0 != (next & 0x40))
                    {
                        count = (next & 0x3F) + 2;
                        byte v = (byte)m_input.ReadByte();
                        for (int i = 0; i < count; ++i)
                            output[dst+i] = v;
                    }
                    else
                    {
                        count = (next & 0x3F) + 1;
                        if (count != m_input.Read (output, dst, count))
                            break;
                    }
                }
                else if (0 == (next & 0x40))
                {
                    count = m_input.ReadByte() | ((next & 0xF) << 8);
                    switch ((next >> 4) & 3)
                    {
                    case 0:
                        {
                            count += 2;
                            byte v = (byte)m_input.ReadByte();
                            for (int i = 0; i < count; ++i)
                                output[dst+i] = v;
                            break;
                        }
                    case 1:
                        ++count;
                        Binary.CopyOverlapped (output, dst-m_width, dst, count);
                        break;
                    case 2:
                        ++count;
                        Binary.CopyOverlapped (output, dst-2*m_width, dst, count);
                        break;
                    case 3:
                        ++count;
                        Binary.CopyOverlapped (output, dst-4*m_width, dst, count);
                        break;
                    }
                }
                else if (0 == (next & 0x20))
                {
                    count = m_input.ReadByte() + ((next & 7) << 8) + 1;
                    switch ((next >> 3) & 3)
                    {
                    case 0:
                        m_input.Read (output, dst, 2);
                        Binary.CopyOverlapped (output, dst, dst+2, count*2);
                        ++count;
                        count *= 2;
                        break;
                    case 1:
                        m_input.Read (output, dst, 4);
                        Binary.CopyOverlapped (output, dst, dst+4, count*4);
                        ++count;
                        count *= 4;
                        break;
                    case 2:
                        m_input.Read (output, dst, 8);
                        Binary.CopyOverlapped (output, dst, dst+8, count*8);
                        ++count;
                        count *= 8;
                        break;
                    case 3:
                        m_input.Read (output, dst, 16);
                        Binary.CopyOverlapped (output, dst, dst+16, count*16);
                        ++count;
                        count *= 16;
                        break;
                    }
                }
                else
                {
                    int v36 = m_input.ReadByte() | ((next & 0xF) << 8);
                    count = m_input.ReadByte() + 1;
                    int src = dst - 1 - v36;
                    Binary.CopyOverlapped (output, src, dst, count);
                }
                dst += count;
            }
            return output;
        }

        private void FlipPixels ()
        {
            // flip pixels vertically
            var pixels = new byte[m_output.Length];
            int dst = 0;
            for (int src = m_stride * (m_height-1); src >= 0; src -= m_stride)
            {
                Buffer.BlockCopy (m_output, src, pixels, dst, m_stride);
                dst += m_stride;
            }
            m_output = pixels;
        }

        void FillBytes (int dst, byte value, int count)
        {
            for (int i = 0; i < count; ++i)
                m_output[dst+i] = value;
        }
    }
}
