//! \file       ImageG.cs
//! \date       2023 Oct 15
//! \brief      System-98 engine image format (PC-98).
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
using System.Windows.Media;

// [951216][Four-Nine] Lilith

namespace GameRes.Formats.System98
{
    [Export(typeof(ImageFormat))]
    public class GFormat : ImageFormat
    {
        public override string         Tag => "G/SYSTEM98";
        public override string Description => "System-98 engine image format";
        public override uint     Signature => 0;

        public GFormat ()
        {
            Extensions = new[] { "g", "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 61)
                return null;
            var header = file.ReadHeader (0xA);
            ushort width  = Binary.BigEndian (header.ToUInt16 (6));
            ushort height = Binary.BigEndian (header.ToUInt16 (8));
            if (0 == width || 0 == height || (width & 7) != 0 || width > 640 || height > 400)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0xA;
            var palette = ReadPalette (file.AsStream, 16, PaletteFormat.Rgb);
            var reader = new GraBaseReader (file, info);
            reader.UnpackBits();
            return ImageData.Create (info, PixelFormats.Indexed4, palette, reader.Pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GFormat.Write not implemented");
        }
    }

    /// <summary>
    /// This compression format is used in several PC-98 game engines.
    /// </summary>
    internal class GraBaseReader
    {
        protected IBinaryStream m_input;
        protected ImageMetaData m_info;
        protected int           m_output_stride;
        protected byte[]        m_pixels;
        protected int           m_dst;

        public byte[] Pixels => m_pixels;
        public int    Stride => m_output_stride;

        public GraBaseReader (IBinaryStream file, ImageMetaData info)
        {
            m_input = file;
            m_info = info;
            m_output_stride = m_info.iWidth >> 1;
            m_pixels = new byte[m_output_stride * m_info.iHeight];
        }

        protected ushort[] m_buffer;

        public void UnpackBits ()
        {
            try
            {
                UnpackBitsInternal();
            }
            catch (EndOfStreamException)
            {
                FlushBuffer();
            }
        }

        void UnpackBitsInternal ()
        {
            int width = m_info.iWidth;
            int wTimes2 = width << 1;
            int wTimes4 = width << 2;
            int buffer_size = wTimes4 + wTimes2;
            m_buffer = new ushort[buffer_size >> 1];
            m_dst = 0;
            InitFrame();
            InitBitReader();
            ushort p = ReadPair (0);
            for (int i = 0; i < width; ++i)
                m_buffer[i] = p;
            int dst = wTimes2;
            int prev_src = 0;
            while (m_dst < m_pixels.Length)
            {
                bool same_line = false;
                int src = -width;
                if (GetNextBit() != 0)
                {
                    if (GetNextBit() == 0)
                        src <<= 1;
                    else if (GetNextBit() == 0)
                        src += 1;
                    else
                        src -= 1;
                }
                else if (GetNextBit() == 0)
                {
                    src = -4;
                    p = m_buffer[dst/2-1];
                    if ((p & 0xFF) == (p >> 8))
                        same_line = src != prev_src;
                }
                if (src != prev_src)
                {
                    prev_src = src;
                    if (!same_line)
                        src += dst;
                    else
                        src = dst - 2;
                    if (GetNextBit() != 0)
                    {
                        int bitlength = 0;
                        do
                        {
                            ++bitlength;
                        }
                        while (GetNextBit() != 0);
                        int count = 1;
                        while (bitlength --> 0)
                            count = count << 1 | GetNextBit();
                        int remaining = (buffer_size - dst) >> 1;
                        while (count > remaining)
                        {
                            count -= remaining;
                            MovePixels (m_buffer, src, dst, remaining);
                            src += remaining << 1;
                            if (FlushBuffer())
                                return;
                            dst = wTimes2;
                            src -= wTimes4;
                            remaining = wTimes4 >> 1;
                        }
                        MovePixels (m_buffer, src, dst, count);
                        dst += count << 1;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = wTimes2;
                        }
                    }
                    else
                    {
                        MovePixels (m_buffer, src, dst, 1);
                        dst += 2;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = wTimes2;
                        }
                    }
                }
                else
                {
                    p = m_buffer[dst/2-1];
                    do
                    {
                        byte prev = (byte)(p >> 8);
                        p = ReadPair (prev);
                        m_buffer[dst >> 1] = p;
                        dst += 2;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = wTimes2;
                        }
                    }
                    while (GetNextBit() != 0);
                    prev_src = 0;
                }
            }
        }

        bool FlushBuffer ()
        {
            MovePixels (m_buffer, m_info.iWidth * 4, 0, m_info.iWidth);
            int src = m_info.iWidth;
            int count = Math.Min (m_info.iWidth << 1, m_pixels.Length - m_dst);
            while (count --> 0)
            {
                ushort p = m_buffer[src++];
                m_pixels[m_dst++] = (byte)((p & 0xF0) | p >> 12);
            }
            return m_dst == m_pixels.Length;
        }

        protected ushort ReadPair (int pos)
        {
            byte al = ReadPixel (pos);
            byte ah = ReadPixel (al);
            return (ushort)(al | ah << 8);
        }

        protected byte ReadPixel (int pos)
        {
            byte px = 0;
            if (GetNextBit() == 0)
            {
                int count = 1;
                if (GetNextBit() != 0)
                {
                    if (GetNextBit() != 0)
                    {
                        count = count << 1 | GetNextBit();
                    }
                    count = count << 1 | GetNextBit();
                }
                count = count << 1 | GetNextBit();
                pos += count;
                px = m_frame[pos--];
                while (count --> 0)
                {
                    m_frame[pos+1] = m_frame[pos];
                    --pos;
                }
                m_frame[pos+1] = px;
            }
            else if (GetNextBit() == 0)
            {
                px = m_frame[pos];
            }
            else
            {
                px = m_frame[pos+1];
                m_frame[pos+1] = m_frame[pos];
                m_frame[pos] = px;
            }
            return px;
        }

        byte[] m_frame;

        protected void InitFrame ()
        {
            m_frame = new byte[0x100];
            int p = 0;
            byte a = 0;
            for (int j = 0; j < 0x10; ++j)
            {
                for (int i = 0; i < 0x10; ++i)
                {
                    m_frame[p++] = a;
                    a -= 0x10;
                }
                a += 0x10;
            }
        }

        protected void MovePixels (ushort[] pixels, int src, int dst, int count)
        {
            count <<= 1;
            if (dst > src)
            {
                while (count > 0)
                {
                    int preceding = Math.Min (dst - src, count);
                    Buffer.BlockCopy (pixels, src, pixels, dst, preceding);
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                Buffer.BlockCopy (pixels, src, pixels, dst, count);
            }
        }

        int m_bits;
        int m_bit_count;

        protected void InitBitReader ()
        {
            m_bit_count = 1;
        }

        protected byte GetNextBit ()
        {
            if (--m_bit_count <= 0)
            {
                m_bits = m_input.ReadUInt8();
                m_bit_count = 8;
            }
            int bit = (m_bits >> 7) & 1;
            m_bits <<= 1;
            return (byte)bit;
        }
    }
}
