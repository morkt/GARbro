//! \file       ImageBITD.cs
//! \date       Fri Jun 26 07:45:01 2015
//! \brief      Selen image format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Macromedia
{
    [Export(typeof(ImageFormat))]
    public class BitdFormat : ImageFormat
    {
        public override string         Tag { get { return "BITD"; } }
        public override string Description { get { return "Selen RLE-compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (stream.Length > 0xffffff)
                return null;
            var scanner = new BitdScanner (stream.AsStream);
            return scanner.GetInfo();
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new BitdReader (stream.AsStream, info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("BitdFormat.Write not implemented");
        }
    }

    internal class BitdScanner
    {
        Stream  m_input;

        protected Stream Input { get { return m_input; } }

        public BitdScanner (Stream input)
        {
            m_input = input;
        }

        const int MaxScanLine = 2048;

        public ImageMetaData GetInfo ()
        {
            int total = 0;
            var scan_lines = new Dictionary<int, int>();
            var key_lines = new List<int>();
            for (;;)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    break;
                int count = b;
                if (b > 0x7f)
                    count = (byte)-(sbyte)b;
                ++count;
                if (count > 0x7f)
                    return null;
                if (b > 0x7f)
                {
                    if (-1 == m_input.ReadByte())
                        return null;
                }
                else
                    m_input.Seek (count, SeekOrigin.Current);

                key_lines.Clear();
                key_lines.AddRange (scan_lines.Keys);
                foreach (var line in key_lines)
                {
                    int width = scan_lines[line];
                    if (width < count)
                        scan_lines.Remove (line);
                    else if (width == count)
                        scan_lines[line] = line;
                    else
                        scan_lines[line] = width - count;
                }

                total += count;
                if (total <= MaxScanLine && total >= 8)
                    scan_lines[total] = total;
                if (total > MaxScanLine && !scan_lines.Any())
                    return null;
            }
            int rem;
            total = Math.DivRem (total, 4, out rem);
            if (rem != 0)
                return null;
            var valid_lines = from line in scan_lines where line.Key == line.Value
                              orderby line.Key
                              select line.Key;
            bool is_eof = -1 == m_input.ReadByte();
            foreach (var width in valid_lines)
            {
                int height = Math.DivRem (total, width, out rem);
                if (0 == rem)
                {
                    return new ImageMetaData
                    {
                        Width = (uint)width,
                        Height = (uint)height,
                        BPP = 32,
                    };
                }
            }
            return null;
        }
    }

    internal class BitdReader : BitdScanner
    {
        byte[]          m_output;
        int             m_width;
        int             m_height;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public BitdReader (Stream input, ImageMetaData info) : base (input)
        {
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_output = new byte[m_width * m_height * 4];
            Format = PixelFormats.Bgra32;
        }

        public void Unpack ()
        {
            int stride = m_width * 4;
            var scan_line = new byte[stride];
            for (int line = 0; line < m_output.Length; line += stride)
            {
                int dst = 0;
                while (dst < stride)
                {
                    int b = Input.ReadByte();
                    if (-1 == b)
                        throw new InvalidFormatException ("Unexpected end of file");
                    int count = b;
                    if (b > 0x7f)
                        count = (byte)-(sbyte)b;
                    ++count;
                    if (dst + count > stride)
                        throw new InvalidFormatException();
                    if (b > 0x7f)
                    {
                        b = Input.ReadByte();
                        if (-1 == b)
                            throw new InvalidFormatException ("Unexpected end of file");
                        for (int i = 0; i < count; ++i)
                            scan_line[dst++] = (byte)b;
                    }
                    else
                    {
                        Input.Read (scan_line, dst, count);
                        dst += count;
                    }
                }
                dst = line;
                for (int x = 0; x < m_width; ++x)
                {
                    m_output[dst++] = scan_line[x+m_width*3];
                    m_output[dst++] = scan_line[x+m_width*2];
                    m_output[dst++] = scan_line[x+m_width];
                    m_output[dst++] = scan_line[x];
                }
            }
        }
    }

    internal class BitdDecoder : IImageDecoder
    {
        Stream  m_input;
        byte[]  m_output;
        int     m_width;
        int     m_height;
        int     m_stride;
        ImageMetaData   m_info;
        ImageData       m_image;
        BitmapPalette   m_palette;

        public Stream            Source => m_input;
        public ImageFormat SourceFormat => null;
        public ImageMetaData       Info => m_info;
        public ImageData          Image => m_image ?? (m_image = GetImageData());
        public PixelFormat       Format { get; private set; }
        public byte[]      AlphaChannel { get; set; }

        public BitdDecoder (Stream input, ImageMetaData info, BitmapPalette palette)
        {
            m_input = input;
            m_info = info;
            m_width = info.iWidth;
            m_height = info.iHeight;
            m_stride = (m_width * m_info.BPP + 7) / 8;
            m_stride = (m_stride + 1) & ~1;
            m_output = new byte[m_stride * m_height];
            Format = info.BPP ==  2 ? PixelFormats.Indexed2
                   : info.BPP ==  4 ? PixelFormats.Indexed4
                   : info.BPP ==  8 ? PixelFormats.Indexed8
                   : info.BPP == 16 ? PixelFormats.Bgr555
                                    : PixelFormats.Bgr32;
            m_palette = palette;
        }

        protected ImageData GetImageData ()
        {
            m_input.Position = 0;
            if (Info.BPP <= 8)
                Unpack8bpp();
            else
                UnpackChannels (Info.BPP / 8);
            if (AlphaChannel != null)
            {
                if (Info.BPP != 32)
                {
                    BitmapSource bitmap = BitmapSource.Create (Info.iWidth, Info.iHeight, ImageData.DefaultDpiX, ImageData.DefaultDpiY, Format, m_palette, m_output, m_stride);
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);
                    m_stride = bitmap.PixelWidth * 4;
                    m_output = new byte[bitmap.PixelHeight * m_stride];
                    bitmap.CopyPixels (m_output, m_stride, 0);
                }
                ApplyAlphaChannel (AlphaChannel);
                Format = PixelFormats.Bgra32;
            }
            return ImageData.Create (m_info, Format, m_palette, m_output, m_stride);
        }

        void ApplyAlphaChannel (byte[] alpha)
        {
            int alpha_stride = (m_width + 1) & ~1;
            int src = 0;
            int pdst = 3;
            for (int y = 0; y < m_height; ++y)
            {
                int dst = pdst;
                for (int x = 0; x < m_width; ++x)
                {
                    m_output[dst] = alpha[src+x];
                    dst += 4;
                }
                src += alpha_stride;
                pdst += m_stride;
            }
        }

        public byte[] Unpack8bpp ()
        {
            for (int line = 0; line < m_output.Length; line += m_stride)
            {
                int x = 0;
                while (x < m_stride)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        throw new InvalidFormatException ("Unexpected end of file");
                    int count = b;
                    if (b > 0x7f)
                        count = (byte)-(sbyte)b;
                    ++count;
                    if (x + count > m_stride)
                        throw new InvalidFormatException();
                    if (b > 0x7f)
                    {
                        b = m_input.ReadByte();
                        if (-1 == b)
                            throw new InvalidFormatException ("Unexpected end of file");
                        for (int i = 0; i < count; ++i)
                            m_output[line + x++] = (byte)b;
                    }
                    else
                    {
                        m_input.Read (m_output, line + x, count);
                        x += count;
                    }
                }
            }
            return m_output;
        }

        public void UnpackChannels (int channels)
        {
            var scan_line = new byte[m_stride];
            for (int line = 0; line < m_output.Length; line += m_stride)
            {
                int x = 0;
                while (x < m_stride)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        throw new InvalidFormatException ("Unexpected end of file");
                    int count = b;
                    if (b > 0x7f)
                        count = (byte)-(sbyte)b;
                    ++count;
                    if (x + count > m_stride)
                        throw new InvalidFormatException();
                    if (b > 0x7f)
                    {
                        b = m_input.ReadByte();
                        if (-1 == b)
                            throw new InvalidFormatException ("Unexpected end of file");
                        for (int i = 0; i < count; ++i)
                            scan_line[x++] = (byte)b;
                    }
                    else
                    {
                        m_input.Read (scan_line, x, count);
                        x += count;
                    }
                }
                int dst = line;
                for (int i = 0; i < m_width; ++i)
                {
                    for (int src = m_width * (channels - 1); src >= 0; src -= m_width)
                        m_output[dst++] = scan_line[i + src];
                }
            }
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
            GC.SuppressFinalize (this);
        }
        #endregion
    }
}
