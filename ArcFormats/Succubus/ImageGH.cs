//! \file       ImageGH.cs
//! \date       2017 Dec 26
//! \brief      Succubus image format.
//
// Copyright (C) 2017-2018 by morkt
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

// [000310][Succubus] Utagoe 2 ~Dance Mix~

namespace GameRes.Formats.Succubus
{
    internal class GhpMetaData : ImageMetaData
    {
        public int  Version;
        public int  Colors;
        public uint PaletteOffset;
        public uint DataOffset;
        public int  ChunkCount;
        public int  OpaqueCount;
        public int  IndexBits;
    }

    [Export(typeof(ImageFormat))]
    public class GhFormat : ImageFormat
    {
        public override string         Tag { get { return "GH"; } }
        public override string Description { get { return "Succubus image format"; } }
        public override uint     Signature { get { return 0x33504847; } }

        public GhFormat ()
        {
            Signatures = new uint[] { 0x33504847, 0x32504847 }; // 'GHP3', 'GHP2'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28);
            int version = header[3] - '0';
            var info = new GhpMetaData {
                Width = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP = 8,
                Colors = header.ToUInt16 (0x10),
                Version = version,
            };
            if (2 == version)
            {
                info.PaletteOffset = header.ToUInt32 (0x14);
                info.ChunkCount = header.ToInt32 (0x18);
                info.DataOffset = header.ToUInt32 (0x1C);
            }
            else
            {
                info.PaletteOffset = header.ToUInt32 (0x18);
                info.DataOffset = header.ToUInt32 (0x24);
                info.OpaqueCount = header.ToInt32 (0x20);
                // the palette size is derived from the offsets; declared counts can be
                // rounded up (253 -> 254), so they are only used as fallbacks
                long palette_size = (long)info.DataOffset - info.PaletteOffset;
                int colors = palette_size > 0 ? (int)(palette_size / 3) : 0;
                if (colors < 1 || colors > 0x1000)
                {
                    uint declared = header.ToUInt32 (0x1C);
                    colors = declared >= 1 && declared <= 0x1000 ? (int)declared : (int)info.Colors;
                }
                info.Colors = colors;
                int max_colors = header.ToUInt16 (0x10);
                info.IndexBits = GhpReader.GetColorDepth (max_colors > 0 ? max_colors : colors);
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GhpReader (file, (GhpMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GhFormat.Write not implemented");
        }
    }

    internal class GhpReader
    {
        IBinaryStream       m_input;
        GhpMetaData         m_info;
        byte[]              m_output;
        int                 m_stride;
        int                 m_depth;

        public GhpReader (IBinaryStream input, GhpMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = ((int)info.Width + 3) & ~3;
            m_depth = GetColorDepth (m_info.Colors);
            m_output = new byte[m_stride * (int)m_info.Height];
        }

        public ImageData Unpack ()
        {
            m_input.Position = m_info.PaletteOffset;
            var palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors, PaletteFormat.Rgb);
            m_input.Position = m_info.DataOffset;
            if (2 == m_info.Version)
                Unpack2();
            else
                Unpack3();
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, m_output, m_stride);
        }

        void Unpack2 ()
        {
            int width = (int)m_info.Width;
            var repeat_table = new uint[(width * (int)m_info.Height + 31) / 32];
            int count = 0;
            int skip = 5;
            int x = 0, y = 0;
            int next_x = 0, next_y = 0;
            ReadPos();
            int pix = ReadBits (m_depth);
            for (int i = 0; i < m_info.ChunkCount; )
            {
                if (count <= 0)
                {
                    int ctl = ReadBits (2);
                    if (ctl > 2)
                        count = ReadCount() - 1;
                    else
                        skip = ReadBits (1) + 2 * ctl;
                }
                else
                {
                    --count;
                }
                m_output[m_stride * y + x] = (byte)pix;
                int rpos = width * y + x;
                repeat_table[rpos >> 5] |= 1u << (rpos & 0x1F);
                if (skip >= 5)
                {
                    int pos = ReadPos() + next_x;
                    next_y += Math.DivRem (pos, width, out next_x);
                    pix = ReadBits (m_depth);
                    y = next_y;
                    x = next_x;
                    ++i;
                }
                else
                {
                    ++y;
                    x += skip - 2;
                }
            }
            pix = 0;
            int src = 0;
            uint bitmap = 0;
            for (y = 0; y < (int)m_info.Height; ++y)
            for (x = 0; x < width; ++x)
            {
                if ((src & 0x1F) == 0)
                    bitmap = repeat_table[src >> 5];
                if ((bitmap & 1) != 0)
                    pix = m_output[m_stride * y + x];
                else
                    m_output[m_stride * y + x] = (byte)pix;
                bitmap >>= 1;
                ++src;
            }
        }

        // GHP3 counts position-advance events; intervening steps can write more pixels.
        // Gaps are filled with the nearest preceding value.
        void Unpack3 ()
        {
            int width = (int)m_info.Width;
            int height = (int)m_info.Height;
            int bits = m_info.IndexBits;
            var repeat_table = new uint[(width * height + 31) / 32];
            int rep = 0;
            int step = 5;
            int x = 0, y = 0;
            int next_x = 0, next_y = 0;
            int count = 0;
            long guard = 32L * ((long)width * height + Math.Abs ((long)m_info.OpaqueCount)) + 0x1000;
            int pix = bits > 0 ? ReadBits (bits) : 0;
            while (count < m_info.OpaqueCount)
            {
                if (--guard < 0) // corrupt stream
                    throw new InvalidFormatException ();
                if (rep <= 0)
                {
                    int ctl = ReadBits (2);
                    if (ctl > 2)
                        rep = ReadCount3 () - 2;
                    else
                        step = ReadBits (1) + 2 * ctl;
                }
                else
                {
                    --rep;
                }
                if (x >= 0 && x < width && y >= 0 && y < height)
                {
                    m_output[m_stride * y + x] = (byte)pix;
                    int rpos = width * y + x;
                    repeat_table[rpos >> 5] |= 1u << (rpos & 0x1F);
                }
                if (step >= 5)
                {
                    if (++count == m_info.OpaqueCount)
                        break;
                    int pos = ReadPos3 () + next_x;
                    next_y += Math.DivRem (pos, width, out next_x);
                    pix = bits > 0 ? ReadBits (bits) : 0;
                    y = next_y;
                    x = next_x;
                }
                else
                {
                    ++y;
                    x += step - 2;
                }
            }
            pix = 0;
            int src = 0;
            uint bitmap = 0;
            for (y = 0; y < height; ++y)
            for (x = 0; x < width; ++x)
            {
                if ((src & 0x1F) == 0)
                    bitmap = repeat_table[src >> 5];
                if ((bitmap & 1) != 0)
                    pix = m_output[m_stride * y + x];
                else
                    m_output[m_stride * y + x] = (byte)pix;
                bitmap >>= 1;
                ++src;
            }
        }

        // repeat length prefix code, returns the run length + 2
        int ReadCount3 ()
        {
            int index = ReadBitCount ();
            if (0 == index)
                return 3;
            if (index >= Count3Bits.Length)
                throw new InvalidFormatException ();
            return Count3Base[index] + ReadBits (Count3Bits[index]) + 4;
        }

        // position prefix code, returns the distance + 1
        int ReadPos3 ()
        {
            int index = ReadBitCount ();
            if (index >= Pos3Bits.Length)
                throw new InvalidFormatException ();
            return Pos3Base[index] + ReadBits (Pos3Bits[index]) + 1;
        }

        // The prefix table ends before the separate pixel mask table at 0x1002E188.
        static readonly int[] Count3Bits = { 0, 2, 4, 6, 8, 12, 16, 18 };
        static readonly int[] Count3Base = { 0, 0, 4, 20, 84, 340, 4436, 69972 };
        static readonly int[]   Pos3Bits = { 2, 4, 6, 8, 12, 16, 18 };
        static readonly int[]   Pos3Base = { 0, 4, 20, 84, 340, 4436, 69972 };

        internal static int GetColorDepth (int colors)
        {
            int depth = 0;
            for (int bit = 1; bit < colors && depth < 24; bit <<= 1)
            {
                ++depth;
            }
            return depth;
        }

        int ReadPos ()
        {
            int pos = ReadBitCount();
            if (pos > 0)
                return BitTable[2 * pos + 3] + ReadBits (BitTable[2 * pos + 2]) + 1;
            else
                return ReadBits (2) + 1;
        }

        int ReadCount ()
        {
            int count = 0;
            int x = ReadBitCount();
            if (x > 0)
                count = BitTable[2 * x + 1] + ReadBits (BitTable[2 * x]) + 1;
            return count + 2;
        }

        uint      m_bits = 0;
        int       m_cached_bits = 0;

        int ReadBits (int count)
        {
            uint bits = 0;
            if (count > m_cached_bits)
            {
                if (m_cached_bits > 0)
                {
                    count -= m_cached_bits;
                    bits = m_bits >> (32 - m_cached_bits) << count;
                }
                FillBitsCache();
            }
            bits |= m_bits >> (32 - count);
            m_bits <<= count;
            m_cached_bits -= count;
            return (int)bits;
        }

        int ReadBitCount ()
        {
            uint count = 0;
            for (;;)
            {
                if (0 == m_cached_bits)
                    FillBitsCache();
                uint bit = m_bits >> 31;
                --m_cached_bits;
                m_bits <<= 1;
                if (0 == bit)
                    break;
                ++count;
            }
            return (int)count;
        }

        void FillBitsCache ()
        {
            m_bits = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    break;
                m_bits |= (uint)b << shift;
            }
            m_cached_bits = 32;
        }

        static readonly int[] BitTable = {
            0, 0, 2, 0, 4, 4, 6, 0x14, 8, 0x54, 0x0C, 0x154, 0x10, 0x1154, 0x12, 0x11154,
        };
    }
}
