//! \file       ImageBITD.cs
//! \date       Fri Jun 26 07:45:01 2015
//! \brief      Macromedia Director image format.
//
// Copyright (C) 2015-2023 by morkt
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
    internal class BitdMetaData : ImageMetaData
    {
        public byte DepthType;
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
        public ImageFormat SourceFormat { get; private set; }
        public ImageMetaData       Info => m_info;
        public ImageData          Image => m_image ?? (m_image = GetImageData());
        public PixelFormat       Format { get; private set; }
        public byte[]      AlphaChannel { get; set; }

        public BitdDecoder (Stream input, BitdMetaData info, BitmapPalette palette)
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
                   :  info.DepthType == 0x87 // i have no clue what this is
                   || info.DepthType == 0x8A ? PixelFormats.Bgra32  // depth type 0x87/0x8A
                                             : PixelFormats.Bgra32; // depth type 0x82/84/85/86/8C
            m_palette = palette;
        }

        private BitdDecoder (Stream input, ImageMetaData info, byte[] alpha_channel)
        {
            m_input = input;
            m_info = info;
            m_width = info.iWidth;
            m_height = info.iHeight;
            m_stride = (m_width * m_info.BPP + 7) / 8;
            Format = PixelFormats.Bgra32;
            AlphaChannel = alpha_channel;
            SourceFormat = ImageFormat.Jpeg;
        }

        public static IImageDecoder FromJpeg (Stream input, ImageMetaData info, byte[] alpha_channel)
        {
            return new BitdDecoder (input, info, alpha_channel);
        }

        protected ImageData GetImageData ()
        {
            BitmapSource bitmap = null;
            m_input.Position = 0;
            if (SourceFormat == ImageFormat.Jpeg)
            {
                var decoder = new JpegBitmapDecoder (m_input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                bitmap = decoder.Frames[0];
                if (null == AlphaChannel)
                    return new ImageData (bitmap, m_info);
            }
            else if (Info.BPP > 8)
                UnpackChannels (Info.BPP / 8);
            else if (m_output.Length != m_input.Length)
                Unpack8bpp();
            else
                m_input.Read (m_output, 0, m_output.Length);

            if (AlphaChannel != null)
            {
                if (Info.BPP != 32 || bitmap != null)
                {
                    if (bitmap == null)
                        bitmap = BitmapSource.Create (m_width, m_height, ImageData.DefaultDpiX, ImageData.DefaultDpiY, Format, m_palette, m_output, m_stride);
                    if (Info.BPP != 32)
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
                UnpackScanLine (m_output, line);
            }
            return m_output;
        }

        public void UnpackChannels (int channels)
        {
            var scan_line = new byte[m_stride];
            for (int line = 0; line < m_output.Length; line += m_stride)
            {
                UnpackScanLine (scan_line, 0);
                int dst = line;
                for (int i = 0; i < m_width; ++i)
                {
                    for (int src = m_width * (channels - 1); src >= 0; src -= m_width)
                        m_output[dst++] = scan_line[i + src];
                }
            }
        }

        void UnpackScanLine (byte[] scan_line, int pos)
        {
            int x = 0;
            while (x < m_stride)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    break; // one in 5000 images somehow stumbles here
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
                        scan_line[pos + x++] = (byte)b;
                }
                else
                {
                    m_input.Read (scan_line, pos+x, count);
                    x += count;
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
