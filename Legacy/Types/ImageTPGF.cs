//! \file       ImageTPGF.cs
//! \date       2018 Jun 09
//! \brief      Types image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

// [991218][Types] Ichou no Mau Koro 2

namespace GameRes.Formats.Types
{
    [Export(typeof(ImageFormat))]
    public class TpgFormat : ImageFormat
    {
        public override string         Tag { get { return "TPGF"; } }
        public override string Description { get { return "Types image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (13);
            if (!header.AsciiEqual (4, "TPGF"))
                return null;
            int bpp = header[12];
            if (bpp != 8 && bpp != 24)
                return null;
            return new ImageMetaData {
                Width  = BigEndian.ToUInt16 (header, 8),
                Height = BigEndian.ToUInt16 (header, 10),
                BPP    = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var tpgf = new TpgfReader (file, info))
            {
                var pixels = tpgf.Unpack();
                return ImageData.Create (info, tpgf.Format, tpgf.Palette, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TpgFormat.Write not implemented");
        }
    }

    internal class TpgfReader : IDisposable
    {
        BitStreamEx     m_input;
        byte[]          m_output;
        ImageMetaData   m_info;
        int             m_stride;
        byte[]          m_scanline;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public TpgfReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = new BitStreamEx (input.AsStream, true);
            m_info = info;
            int bpp = m_info.BPP;
            if (24 == bpp)
                bpp = 32;
            m_stride = (int)m_info.Width * (bpp / 8);
            m_output = new byte[m_stride * (int)m_info.Height];
            m_scanline = new byte[m_info.Width];
        }

        public byte[] Unpack ()
        {
            m_input.Reset();
            m_input.Input.Position = 13;
            if (8 == m_info.BPP)
            {
                Unpack8bpp (m_output);
                Format = PixelFormats.Gray8;
                return m_output; // alpha channel ignored for 8bpp bitmaps
            }
            else
            {
                Unpack24bpp (m_output);
                Format = PixelFormats.Bgr32;
            }
            if (m_input.Input.ReadByte() == 1) // alpha data
            {
                var header = new byte[13];
                m_input.Input.Read (header, 0, 13);
                if (header.AsciiEqual (4, "TPGF"))
                {
                    uint width  = BigEndian.ToUInt16 (header, 8);
                    uint height = BigEndian.ToUInt16 (header, 10);
                    int bpp = header[12];
                    if (width == m_info.Width && height == m_info.Height && bpp == 8)
                    {
                        m_input.Reset();
                        UnpackAlpha (m_output);
                        Format = PixelFormats.Bgra32;
                    }
                }
            }
            return m_output;
        }

        void Unpack24bpp (byte[] output)
        {
            int dst_line = 0;
            for (uint y = 0; y < m_info.Height; ++y)
            {
                for (int i = 0; i < 3; ++i)
                {
                    ReadScanLine (m_scanline);
                    TransformLine (m_scanline);
                    int dst = dst_line + i;
                    for (uint x = 0; x < m_info.Width; ++x)
                    {
                        output[dst] = m_scanline[x];
                        dst += 4;
                    }
                }
                dst_line += m_stride;
            }
        }

        void Unpack8bpp (byte[] output)
        {
            int width = (int)m_info.Width;
            int dst = 0;
            for (uint y = 0; y < m_info.Height; ++y)
            {
                ReadScanLine (m_scanline);
                TransformLine (m_scanline);
                for (int x = 0; x < width; ++x)
                {
                    output[dst + x] = m_scanline[x];
                }
                dst += m_stride;
            }
        }

        void UnpackAlpha (byte[] output)
        {
            int dst_line = 3;
            for (uint y = 0; y < m_info.Height; ++y)
            {
                ReadScanLine (m_scanline);
                TransformLine (m_scanline);
                int dst = dst_line;
                for (uint x = 0; x < m_info.Width; ++x)
                {
                    output[dst] = (byte)~m_scanline[x];
                    dst += 4;
                }
                dst_line += m_stride;
            }
        }

        void ReadScanLine (byte[] line)
        {
            int dst = 0;
            while (dst < line.Length)
            {
                int ctl = m_input.GetBits (3);
                int count = ReadCount() + 1;
                if (ctl != 0)
                {
                    m_input.ReadEncodedBits (line, dst, count, ctl + 1);
                }
                else
                {
                    for (int i = 0; i < count; ++i)
                        line[dst+i] = 0;
                }
                dst += count;
            }
        }

        int ReadCount ()
        {
            int i = 1;
            while (m_input.GetNextBit() == 0)
                ++i;
            return m_input.GetBits (i) + (1 << i) - 2;
        }

        void TransformLine (byte[] line)
        {
            for (int i = 1; i < line.Length; ++i)
            {
                byte a = line[i];
                byte b = line[i-1];
                line[i] = TransformMap[a, b];
            }
        }

        static readonly byte[,] TransformMap = InitTransformMap();

        static byte[,] InitTransformMap ()
        {
            var table = new byte[256,256];
            for (int i = 0; i < 256; ++i)
            for (int j = 0; j < 256; ++j)
            {
                int v;
                if (j >= 128)
                    v = (-1 - j) & 0xFF;
                else
                    v = j;
                if (2 * v < i)
                    v = i;
                else if ((i & 1) != 0)
                    v += (i + 1) >> 1;
                else
                    v -= i >> 1;

                if (j >= 128)
                    table[i,j] = (byte)(-1 - v);
                else
                    table[i,j] = (byte)v;
            }
            return table;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }

    internal class BitStreamEx : BitStream, IBitStream
    {
        public BitStreamEx (Stream file, bool leave_open = false) : base (file, leave_open)
        {
        }

        public int GetBits (int count)
        {
            int mask = (1 << count) - 1;
            int v = 0;
            for (;;)
            {
                if (0 == m_cached_bits)
                {
                    m_bits = m_input.ReadByte();
                    if (-1 == m_bits)
                        return -1;
                    m_cached_bits = 8;
                }
                if (m_cached_bits >= count)
                    break;
                count -= m_cached_bits;
                v |= m_bits << count;
                m_cached_bits = 0;
            }
            m_cached_bits -= count;
            return (m_bits >> m_cached_bits | v) & mask;
        }

        public int GetNextBit ()
        {
            return GetBits (1);
        }

        byte[] m_bit_buffer = new byte[1024];

        public void ReadEncodedBits (byte[] buffer, int dst, int count, int ctl)
        {
            int mask = (1 << ctl) - 1;
            var cur_pos = m_input.Position;
            int byte_count = 0;
            m_input.Read (m_bit_buffer, 0, (count * ctl + 7) / 8 + 1);
            for (int i = 0; i < count; ++i)
            {
                int v = 0;
                int bit_count = ctl;
                for (;;)
                {
                    if (0 == m_cached_bits)
                    {
                        m_bits = m_bit_buffer[byte_count++];
                        m_cached_bits = 8;
                    }
                    if (m_cached_bits >= bit_count)
                        break;
                    bit_count -= m_cached_bits;
                    v |= BitMap1[m_bits, bit_count];
                    m_cached_bits = 0;
                }
                m_cached_bits -= bit_count;
                buffer[dst+i] = (byte)((BitMap2[m_bits, m_cached_bits] | v) & mask);
            }
            m_input.Position = cur_pos + byte_count;
        }

        static readonly byte[,] BitMap1;
        static readonly byte[,] BitMap2;

        static BitStreamEx ()
        {
            BitMap1 = new byte[256,8];
            BitMap2 = new byte[256,8];
            for (int i = 0; i < 256; ++i)
            {
                for (int j = 0; j < 8; ++j)
                {
                    BitMap1[i, j] = (byte)(i << j);
                    BitMap2[i, j] = (byte)(i >> j);
                }
            }
        }
    }
}
