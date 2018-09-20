//! \file       ImageDAT.cs
//! \date       2018 Sep 20
//! \brief      Lazycrew image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Lazycrew
{
    internal class LcImageMetaData : ImageMetaData
    {
        public int  Format;
        public int  Compression;
        public int  DataOffset;

        public bool IsCompressed { get { return (Compression & 0x8000) != 0; } }
        public bool     HasAlpha { get { return Format == 2 || Format == 3; } }
    }

    internal class LcImageDecoder : BinaryImageDecoder
    {
        LcImageMetaData m_info;
        int             m_stride;
        BitmapPalette   m_palette;

        public LcImageDecoder (IBinaryStream input, LcImageMetaData info) : base (input, info)
        {
            m_info = info;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_info.DataOffset;
            if (m_info.BPP <= 8)
            {
                int colors = m_input.ReadUInt16();
                if (m_info.Format != 4)
                    m_palette = ImageFormat.ReadPalette (m_input.AsStream, colors);
                else // grayscale image - ignore palette
                    m_input.Seek (colors * 4, SeekOrigin.Current);
            }
            m_stride = (int)m_info.Width * m_info.BPP / 8;
            var pixels = new byte[m_stride * (int)m_info.Height];

            if (24 == m_info.BPP)
                Unpack24bpp (pixels);
            else if (m_info.IsCompressed)
                UnpackRle (pixels);
            else
                m_input.Read (pixels, 0, pixels.Length);

            if (m_info.HasAlpha && m_info.BPP != 4)
            {
                var header = m_input.ReadBytes (8);
                bool is_compressed = (header[1] & 0x80) != 0;
                int colors = header.ToUInt16 (6);
                m_input.Seek (colors * 4, SeekOrigin.Current);
                var alpha = new byte[(int)m_info.Width * (int)m_info.Height];
                if (is_compressed)
                    UnpackRle (alpha);
                else
                    m_input.Read (alpha, 0, pixels.Length);
                return ApplyAlpha (pixels, alpha);
            }
            return ImageData.Create (m_info, GetPixelFormat(), m_palette, pixels, m_stride);
        }

        ImageData ApplyAlpha (byte[] rgb, byte[] alpha)
        {
            int stride = (int)m_info.Width * 4;
            var output = new byte[stride * (int)m_info.Height];
            int dst = 0;
            int src = 0;
            int asrc = 0;
            if (8 == m_info.BPP)
            {
                var color_map = m_palette.Colors;
                while (dst < output.Length)
                {
                    var color = color_map[rgb[src++]];
                    output[dst++] = color.B;
                    output[dst++] = color.G;
                    output[dst++] = color.R;
                    output[dst++] = alpha[asrc++];
                }
            }
            else
            {
                while (dst < output.Length)
                {
                    output[dst++] = rgb[src++];
                    output[dst++] = rgb[src++];
                    output[dst++] = rgb[src++];
                    output[dst++] = alpha[asrc++];
                }
            }
            m_info.BPP = 32;
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, output, stride);
        }

        void UnpackRle (byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count = m_input.ReadByte();
                if (-1 == count)
                    break;
                if (count > 0x7F)
                {
                    count = (count & 0x7F) + 1;
                    byte v = m_input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                        output[dst+i] = v;
                }
                else
                {
                    m_input.Read (output, dst, ++count);
                }
                dst += count;
            }
        }

        void Unpack24bpp (byte[] output)
        {
            var color = new byte[6];
            int dst = 0;
            while (dst < output.Length)
            {
                int dst1 = dst + m_stride;
                for (int x = 0; x < m_stride; x += 6)
                {
                    if (m_input.Read (color, 0, 6) < 6)
                        return;
                    int r = (10638 * (sbyte)color[1] + 18651 * (sbyte)color[0]) >> 14;
                    int b = (29145 * (sbyte)color[1] - 21601 * (sbyte)color[0]) >> 14;
                    int g = (-5312 * (sbyte)color[0] - 11083 * (sbyte)color[1]) >> 14;
                    output[dst++] = RgbClamp (color[2] + b);
                    output[dst++] = RgbClamp (color[2] + g);
                    output[dst++] = RgbClamp (color[2] + r);
                    output[dst++] = RgbClamp (color[3] + b);
                    output[dst++] = RgbClamp (color[3] + g);
                    output[dst++] = RgbClamp (color[3] + r);
                    output[dst1++] = RgbClamp (color[4] + b);
                    output[dst1++] = RgbClamp (color[4] + g);
                    output[dst1++] = RgbClamp (color[4] + r);
                    output[dst1++] = RgbClamp (color[5] + b);
                    output[dst1++] = RgbClamp (color[5] + g);
                    output[dst1++] = RgbClamp (color[5] + r);
                }
                dst += m_stride;
            }
        }

        static byte RgbClamp (int color)
        {
            if (color < 0)
                return 0;
            else if (color > 0xFF)
                return 0xFF;
            else
                return (byte)color;
        }

        PixelFormat GetPixelFormat ()
        {
            if (4 == m_info.Format)
                return PixelFormats.Gray8;
            switch (m_info.BPP)
            {
            case 4:  return PixelFormats.Indexed4;
            case 8:  return PixelFormats.Indexed8;
            case 24: return PixelFormats.Bgr24;
            default: throw new InvalidFormatException();
            }
        }
    }
}
