//! \file       PdtBitmap.cs
//! \date       2023 Oct 19
//! \brief      UK2 engine compressed bitmap.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [971031][AyPio] Satyr 95

namespace GameRes.Formats.AyPio
{
    [Export(typeof(ImageFormat))]
    public class PdtBmpFormat : ImageFormat
    {
        public override string         Tag => "PDT/BMP";
        public override string Description => "UK2 engine compressed bitmap";
        public override uint     Signature => 0x544450; // 'PDT'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (header.ToInt32 (4) != 0x118)
                return null;
            return new ImageMetaData { Width = 640, Height = 480, BPP = 32 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PdtBmpDecoder (file, info);
            return decoder.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PdtFormat.Write not implemented");
        }
    }

    internal sealed class PdtBmpDecoder
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;

        public PdtBmpDecoder (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        int m_unpacked_size;
        int m_packed_size;

        public ImageData Unpack ()
        {
            long offset = 0;
            var bitmap = UnpackBitmap (offset);
            m_info.Width  = (uint)bitmap.PixelWidth;
            m_info.Height = (uint)bitmap.PixelHeight;
            m_info.BPP = bitmap.Format.BitsPerPixel;
            offset += m_packed_size;
            var signature = m_input.ReadBytes (4);
            if (signature.Length != 4 || !signature.AsciiEqual ("PDT\0"))
                return new ImageData (bitmap, m_info);
            var alpha = UnpackBitmap (offset);
            if (alpha.Format != PixelFormats.Gray8)
                alpha = new FormatConvertedBitmap (alpha, PixelFormats.Gray8, null, 0);
            if (m_info.BPP != 32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);

            int stride = m_info.iWidth * 4;
            var pixels = new byte[stride * m_info.iHeight];
            bitmap.CopyPixels (pixels, stride, 0);
            var rect = new Int32Rect (0, 0, Math.Min (m_info.iWidth, alpha.PixelWidth),
                                      Math.Min (m_info.iHeight, alpha.PixelHeight));
            var a = new byte[m_info.iWidth * m_info.iHeight];
            alpha.CopyPixels (rect, a, m_info.iWidth, 0);
            int src = 0;
            for (int dst = 3; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = a[src++];
            }
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels, stride);
        }

        byte[]  m_bits;
        byte[]  m_output;

        BitmapSource UnpackBitmap (long offset)
        {
            m_input.Position = offset+8;
            m_unpacked_size = m_input.ReadInt32();
            m_packed_size = m_input.ReadInt32();
            long data_offset = m_input.ReadUInt32() + offset;
            long bits_offset = m_input.ReadUInt32() + offset;
            string name = m_input.ReadCString (0x100);

            if (null == m_output || m_unpacked_size > m_output.Length)
                m_output = new byte[m_unpacked_size];
            int bits_length = (int)(data_offset - bits_offset);
            if (null == m_bits || bits_length > m_bits.Length)
                m_bits = new byte[bits_length+4];

            m_input.Position = bits_offset;
            m_input.Read (m_bits, 0, bits_length);

            m_input.Position = data_offset;
            UnpackBits();

            using (var bmp_input = new BinMemoryStream (m_output, name))
            {
                var decoder = new BmpBitmapDecoder (bmp_input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames[0];
            }
        }

        public void UnpackBits ()
        {
            InitBitReader();
            int dst = 0;
            byte last_byte = 0;
            while (dst < m_unpacked_size)
            {
                int ctl = 0;
                while (GetNextBit() != 0)
                    ++ctl;
                switch (ctl)
                {
                case 0:
                    last_byte = m_output[dst++] = m_input.ReadUInt8();
                    break;
                case 1:
                    {
                        int off = GetInteger();
                        int count = GetInteger();
                        Binary.CopyOverlapped (m_output, dst - off, dst, count);
                        dst += count;
                        break;
                    }
                case 2:
                    {
                        int count = GetInteger();
                        int step = GetInteger();
                        int pos = 0;
                        for (int i = 0; i < step; i += count)
                        {
                            Binary.CopyOverlapped (m_output, dst - count, dst + pos, count);
                            pos += count * count;
                        }
                        dst += count * step;
                        break;
                    }
                case 3:
                    m_output[dst++] = last_byte;
                    break;
                }
            }
        }

        int GetInteger ()
        {
            int i = 0;
            while (GetNextBit() != 0)
                ++i;
            int n = 0;
            for (int j = i; j > 0; --j)
            {
                n = n << 1 | GetNextBit();
            }
            return n + (1 << i);
        }

        uint m_current_bits;
        int m_bit_count;
        int m_bit_pos;

        void InitBitReader ()
        {
            m_bit_pos = 0;
            m_bit_count = 0;
        }

        byte GetNextBit ()
        {
            if (0 == m_bit_count--)
            {
                m_current_bits = m_bits.ToUInt32 (m_bit_pos);
                m_bit_pos += 4;
                m_bit_count = 31;
            }
            uint bit = m_current_bits >> 31;
            m_current_bits <<= 1;
            return (byte)bit;
        }
    }
}
