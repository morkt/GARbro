//! \file       ImageGRA.cs
//! \date       2023 Oct 11
//! \brief      Tiare image format (PC-98).
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
using System.Windows.Media.Imaging;

// [950616][JAST] Tenshi-tachi no Gogo ~Tenkousei~
// [950922][Tiare] Vanishing Point -Tenshi no Kieta Machi-

namespace GameRes.Formats.Tiare
{
    internal class GraMetaData : ImageMetaData
    {
        public byte Flags;
        public long DataOffset;

        public bool HasPalette => (Flags & 0x80) == 0;
    }

    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag => "GRA/TIARE";
        public override string Description => "Tiare image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            int pos = header.IndexOf (0x1A);
            if (-1 == pos)
                return null;
            ++pos;
            while (pos < header.Length && header[pos++] != 0)
                ;
            if (pos + 3 >= header.Length || header[pos+3] != 4)
                return null;
            byte flags = header[pos];
            file.Position = pos + 8;
            int skip = Binary.BigEndian (file.ReadUInt16());
            if (skip != 0)
                file.Seek (skip, SeekOrigin.Current);
            uint width  = Binary.BigEndian (file.ReadUInt16());
            uint height = Binary.BigEndian (file.ReadUInt16());
            if (width == 0 || height == 0)
                return null;
            return new GraMetaData
            {
                Width  = width,
                Height = height,
                BPP    = 4,
                Flags  = flags,
                DataOffset = file.Position,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GraReader (file, (GraMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GraFormat.Write not implemented");
        }
    }

    internal class GraReader
    {
        IBinaryStream   m_input;
        GraMetaData     m_info;

        public GraReader (IBinaryStream file, GraMetaData info)
        {
            m_input = file;
            m_info = info;
        }

        byte[] m_pixels;
        ushort[] m_buffer;

        public ImageData Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            BitmapPalette palette;
            if (m_info.HasPalette)
                palette = ImageFormat.ReadPalette (m_input.AsStream, 16, PaletteFormat.Rgb);
            else
                palette = DefaultPalette;
            int width = m_info.iWidth;
            int wTimes2 = width << 1;
            int wTimes4 = width << 2;
            int buffer_size = wTimes4 + wTimes2;
            m_buffer = new ushort[buffer_size >> 1];
            int stride = width >> 1;
            m_pixels = new byte[stride * m_info.iHeight];
            m_dst = 0;
            InitFrame(); // 1BF0:32C
            InitBitReader();
            ushort p = ReadPair (0);
            for (int i = 0; i < width; ++i)
                m_buffer[i] = p;
            int dst = wTimes2;
            int prev_src = 0;
            while (m_dst < m_pixels.Length)
            {
                bool same_line = false;
                int src;
                if (GetNextBit() != 0)
                {
                    src = -width << 1;
                    if (GetNextBit() != 0)
                    {
                        src = -width + 1;
                        if (GetNextBit() != 0)
                        {
                            src -= 2;
                        }
                    }
                }
                else
                {
                    src = -width;
                    if (GetNextBit() == 0)
                    {
                        src = -4;
                        p = m_buffer[dst/2-1];
                        if ((p & 0xFF) == (p >> 8))
                            same_line = src != prev_src;
                    }
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
                            FlushBuffer();
                            dst = wTimes2;
                            src -= wTimes4;
                            remaining = wTimes4 >> 1;
                        }
                        MovePixels (m_buffer, src, dst, count);
                        dst += count << 1;
                        if (dst == buffer_size)
                        {
                            FlushBuffer();
                            dst = wTimes2;
                        }
                    }
                    else
                    {
                        MovePixels (m_buffer, src, dst, 1);
                        dst += 2;
                        if (dst == buffer_size)
                        {
                            FlushBuffer();
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
                            FlushBuffer();
                            dst = wTimes2;
                        }
                    }
                    while (GetNextBit() != 0);
                    prev_src = 0;
                }
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, m_pixels, stride);
        }

        int m_dst;

        void FlushBuffer ()
        {
            MovePixels (m_buffer, m_info.iWidth * 4, 0, m_info.iWidth);
            int src = m_info.iWidth;
            int count = Math.Min (m_info.iWidth << 1, m_pixels.Length - m_dst);
            while (count --> 0)
            {
                ushort p = m_buffer[src++];
                m_pixels[m_dst++] = (byte)((p & 0xF0) | p >> 12);
            }
        }

        ushort ReadPair (int pos)
        {
            byte al = ReadPixel (pos);
            byte ah = ReadPixel (al);
            return (ushort)(al | ah << 8);
        }

        byte ReadPixel (int pos)
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

        void InitFrame ()
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

        void MovePixels (ushort[] pixels, int src, int dst, int count)
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

        void InitBitReader ()
        {
            m_bit_count = 1;
        }

        byte GetNextBit ()
        {
            if (--m_bit_count <= 0)
            {
                m_bits = m_input.ReadByte();
                if (-1 == m_bits)
                    m_bits = 0;
                m_bit_count = 8;
            }
            int bit = (m_bits >> 7) & 1;
            m_bits <<= 1;
            return (byte)bit;
        }

        static readonly BitmapPalette DefaultPalette = new BitmapPalette (new Color[] {
            #region Default palette
            Color.FromRgb (0x00, 0x00, 0x00),
            Color.FromRgb (0x00, 0x00, 0x77),
            Color.FromRgb (0x77, 0x00, 0x00),
            Color.FromRgb (0x77, 0x00, 0x77),
            Color.FromRgb (0x00, 0x77, 0x00),
            Color.FromRgb (0x00, 0x77, 0x77),
            Color.FromRgb (0x77, 0x77, 0x00),
            Color.FromRgb (0x77, 0x77, 0x77),
            Color.FromRgb (0x00, 0x00, 0x00),
            Color.FromRgb (0x00, 0x00, 0xFF),
            Color.FromRgb (0xFF, 0x00, 0x00),
            Color.FromRgb (0xFF, 0x00, 0xFF),
            Color.FromRgb (0x00, 0xFF, 0x00),
            Color.FromRgb (0x00, 0xFF, 0xFF),
            Color.FromRgb (0xFF, 0xFF, 0x00),
            Color.FromRgb (0xFF, 0xFF, 0xFF),
            #endregion
        });
    }
}
