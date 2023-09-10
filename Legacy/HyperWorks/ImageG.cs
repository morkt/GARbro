//! \file       ImageG.cs
//! \date       2023 Aug 28
//! \brief      HyperWorks image format.
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

// [951207][Love Gun] ACE OF SPADES
// I24 predecessor

namespace GameRes.Formats.HyperWorks
{
    [Export(typeof(ImageFormat))]
    public class GFormat : ImageFormat
    {
        public override string         Tag => "G";
        public override string Description => "HyperWorks indexed image format";
        public override uint     Signature => 0x1A477D00;

        public GFormat ()
        {
            Signatures = new[] { 0x1A477D00u, 0u };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            if (header.ToUInt16 (2) != 0x1A47)
                return null;
            // not sure if 0x7D00 is a required signature, so rely on filename
            if (header.ToUInt16 (0) != 0x7D00 && !file.Name.HasExtension (".G"))
                return null;
            return new ImageMetaData {
                Width = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                OffsetX = header.ToInt16 (4),
                OffsetY = header.ToInt16 (6),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GFormat.Write not implemented");
        }
    }

    internal sealed class GReader
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;
        int             m_stride;
        byte[]          m_palette_data;
        byte[]          m_output;
        ushort[]        m_buffer;
        int[]           m_line_ptr;

        public GReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = (m_info.iWidth * m_info.BPP / 8 + 1) & -2;
            int s = Math.Max (0x142, m_stride / 2 + 2); // line buffer size
            m_buffer = new ushort[s * 3 + 1];
            m_line_ptr = new int[3] { 1, 1 + s, 1 + s*2 };
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x0C;
            m_palette_data = m_input.ReadBytes (0x30);
            m_input.Position = 0x40;
            int width = ((m_info.iWidth + 7) & -8);
            int rows = ((m_info.iHeight + 1) & -2);
            m_output = new byte[m_stride * rows];
            InitColorTable();
            int blockW = width >> 1;
            int blockH = rows >> 1;
            SetupBitReader();
            for (int y = 0; y < blockH; ++y)
            {
                var dst = m_line_ptr[2];
                m_line_ptr[2] = m_line_ptr[1];
                m_line_ptr[1] = m_line_ptr[0];
                m_line_ptr[0] = dst;
                int x = 0;
                while (x < blockW)
                {
                    if (GetNextBit() != 0)
                    {
                        m_buffer[dst++] = GetColorFromTable (x);
                        ++x;
                    }
                    else
                    {
                        int count = ExtractBits (BitTable1);
                        if (count >= 0x40)
                            count += ExtractBits (BitTable1);
                        int idx = ExtractBits (BitTable2) * 2;
                        int src = m_line_ptr[OffTable[idx + 1]];
                        src += OffTable[idx] + x;
                        x += count;
                        while (count --> 0)
                        {
                            m_buffer[dst++] = m_buffer[src++];
                        }
                    }
                }
                UnpackRow (y, blockW, m_line_ptr[0]);
            }
            var palette = UnpackPalette();
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, m_output, m_stride);
        }

        void UnpackRow (int y, int width, int buf_pos)
        {
            int row1 = m_stride * y * 2;
            int row2 = row1 + m_stride;
            for (int i = 0; i < width; ++i)
            {
                ushort v = m_buffer[buf_pos++];
                ushort v0 = (ushort)((v & 0xF00 | ((v & 0xF000) >> 12)) + 0xA0A);
                LittleEndian.Pack (v0, m_output, row2);
                row2 += 2;
                ushort v1 = (ushort)((((v & 0xF) << 8) | ((v & 0xF0) >> 4)) + 0xA0A);
                LittleEndian.Pack (v1, m_output, row1);
                row1 += 2;
            }
        }

        byte[] g_palIndexes = { 0, 1, 4, 5, 2, 3, 6, 7, 8, 9, 0xC, 0xD, 0xA, 0xB, 0xE, 0xF };

        BitmapPalette UnpackPalette ()
        {
            var colors = new Color[42];
            for (int i = 0; i < 16; ++i)
            {
                int R = m_palette_data[3 * g_palIndexes[i]    ]; R |= R << 4;
                int G = m_palette_data[3 * g_palIndexes[i] + 1]; G |= G << 4;
                int B = m_palette_data[3 * g_palIndexes[i] + 2]; B |= B << 4;
                colors[i+10] = Color.FromRgb ((byte)R, (byte)G, (byte)B);

                int b = R & 1;
                int c = (sbyte)R >> 1;
                if (c < 0)
                    c += b;
                colors[i+26].R = (byte)c;

                b = G & 1;
                c = (sbyte)G >> 1;
                if (c < 0)
                    c += b;
                colors[i+26].G = (byte)c;

                b = B & 1;
                c = (sbyte)B >> 1;
                if (c < 0)
                    c += b;
                colors[i+26].B = (byte)c;
            }
            return new BitmapPalette (colors);
        }

        byte[] g_colorTable = new byte[256];

        void InitColorTable ()
        {
            int dst = 0;
            for (int i = 0; i < 16; ++i)
                for (int j = 0; j < 16; ++j)
                    g_colorTable[dst++] = (byte)((j + i + 1) & 0xF);
        }

        ushort GetColorFromTable (int x)
        {
            ushort b0 = m_buffer[m_line_ptr[1] + x];
            int n0 =  b0 & 0xF;
            int n1 = (b0 >> 4) & 0xF;
            int n2 = (b0 >> 8) & 0xF;
            int n3 = (b0 >> 12) & 0xF;

            ushort b1 = m_buffer[m_line_ptr[1] + x - 1];
            int m0 =  b1 & 0xF;
            int m1 = (b1 >> 4) & 0xF;
            int m2 = (b1 >> 8) & 0xF;
            int m3 = (b1 >> 12) & 0xF;

            ushort b2 = m_buffer[m_line_ptr[0] + x - 1];
            int p0 =  b2 & 0xF;
            int p1 = (b2 >> 4) & 0xF;
            int p2 = (b2 >> 8) & 0xF;
            int p3 = (b2 >> 12) & 0xF;

            int r1 = n1;
            if (n1 != n3 && (n1 != m1 || n1 != p1))
            {
                if (p0 == p1)
                    r1 = p0;
                else
                    r1 = m2;
            }
            if (GetNextBit() != 0)
                r1 = AdjustColorTable (r1);

            int r0 = n0;
            if (n0 != n2 && (n0 != m0 || n0 != p0))
            {
                if (r1 == p1)
                    r0 = p1;
                else
                    r0 = n3;
            }
            if (GetNextBit() != 0)
                r0 = AdjustColorTable (r0);

            int r3 = n3;
            if (r1 != n3 && (n3 != m3 || n3 != p3))
            {
                if (p2 == p3)
                    r3 = p2;
                else
                    r3 = p0;
            }
            if (GetNextBit() != 0)
                r3 = AdjustColorTable (r3);

            int r2 = n2;
            if (n2 != r0 && (n2 != m2 || n2 != p2))
            {
                if (p3 == r3)
                    r2 = p3;
                else
                    r2 = r1;
            }
            if (GetNextBit() != 0)
                r2 = AdjustColorTable (r2);

            return (ushort)((r3 << 12) | (r2 << 8) | (r1 << 4) | r0);
        }

        byte AdjustColorTable (int idx)
        {
            int shift_count = ExtractBits (BitTable3);
            int i = 16 * idx + shift_count;
            byte c = g_colorTable[i];
            if (shift_count != 0)
            {
                while (shift_count --> 0)
                {
                    g_colorTable[i] = g_colorTable[i-1];
                    --i;
                }
                g_colorTable[i] = c;
            }
            return c;
        }

        int ExtractBits (byte[] table)
        {
            int idx = ((bits >> 8) & 0xFF) << 1;
            int n = table[idx];
            if (n != 0)
            {
                if (n >= bitCount)
                {
                    n -= bitCount;
                    bits <<= bitCount;
                    int b = m_input.ReadByte();
                    if (b != -1) // XXX ignore EOF
                        bits |= b;
                    bitCount = 8;
                }
                bits <<= n;
                bitCount -= n;
                return table[idx+1];
            }
            else
            {
                bits <<= bitCount;
                int b = m_input.ReadByte();
                if (b != -1) // XXX ignore EOF
                    bits |= b;
                bits <<= 8 - bitCount;
                int t = table[idx+1];
                do
                {
                    int i = GetNextBit();
                    t = OffTable[2 * t + 54 + i];
                }
                while (OffTable[2 * t + 54] != 0);
                return OffTable[2 * t + 55];
            }
        }

        int bits;
        int bitCount;

        private void SetupBitReader ()
        {
            bits  = m_input.ReadUInt8() << 8;
            bits |= m_input.ReadUInt8();
            bitCount = 8;
        }

        private int GetNextBit ()
        {
            bits <<= 1;
            if (0 == --bitCount)
            {
                int b = m_input.ReadByte();
                if (b != -1) // XXX ignore EOF
                    bits |= b;
                bitCount = 8;
            }
            return (bits >> 16) & 1;
        }

        static readonly byte[] BitTable1 = new byte[] {
            3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
            2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 8,
            0xD, 0, 0, 0, 1, 8, 0xE, 6, 7, 6, 7, 6, 7, 6, 7, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            7, 0xA, 7, 0xA, 0, 2, 0, 7, 7, 0xB, 7, 0xB, 8, 0xF, 0, 3, 6, 8, 6, 8, 6, 8, 6, 8, 8, 0x10, 0, 4,
            0, 5, 8, 0x11, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 4, 4, 4, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 7, 0xC, 7, 0xC, 0, 8, 0, 6, 6, 9, 6,
            9, 6, 9, 6, 9, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        };
        static readonly byte[] BitTable2 = new byte[] {
            5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 7, 0x17, 7, 0x17, 7, 0x16, 7, 0x16, 6, 0x0E, 6,
            0x0E, 6, 0x0E, 6, 0x0E, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4,
            3, 4, 3, 4, 3, 4, 3, 6, 0x0D, 6, 0x0D, 6, 0x0D, 6, 0x0D, 6, 0x0C, 6, 0x0C, 6, 0x0C, 6, 0x0C, 5, 6,
            5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 6, 5, 7, 5, 7, 5, 7, 5, 7, 5, 7, 5, 7, 5, 7, 5, 7, 5, 5, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 5, 8, 7, 0x14, 7,
            0x14, 7, 0x18, 7, 0x18, 6, 0x10, 6, 0x10, 6, 0x10, 6, 0x10, 6, 0x11, 6, 0x11, 6, 0x11, 6, 0x11, 6,
            0x0F, 6, 0x0F, 6, 0x0F, 6, 0x0F, 5, 0x0A, 5, 0x0A, 5, 0x0A, 5, 0x0A, 5, 0x0A, 5, 0x0A, 5, 0x0A, 5,
            0x0A, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3,
            1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1, 3, 1,
            6, 0x13, 6, 0x13, 6, 0x13, 6, 0x13, 6, 0x12, 6, 0x12, 6, 0x12, 6, 0x12, 5, 0x0B, 5, 0x0B, 5, 0x0B,
            5, 0x0B, 5, 0x0B, 5, 0x0B, 5, 0x0B, 5, 0x0B, 7, 0x19, 7, 0x19, 7, 0x1A, 7, 0x1A, 6, 0x15, 6, 0x15,
            6, 0x15, 6, 0x15, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 5, 9, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
            2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2,
            3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2,
            0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0,
            2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2,
            0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0,
            2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0, 2, 0,
        };
        static readonly byte[] BitTable3 = new byte[] {
            4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 5,
            4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 5, 4, 6, 6, 6, 6, 6, 6, 6, 6, 7, 8, 7, 8, 8, 0x0A, 8, 0x0B,
            4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 4, 3, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 7, 6, 7, 6, 7, 6, 7, 7, 9, 7, 9, 0, 0x5F, 8, 0x0C,
            2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
            1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1,
            2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2,
            1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 1, 0, 1, 0,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
            1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
            0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
        };
        static readonly short[] OffTable = new short[] {
            0, 1, 1, 1, -1, 0, -1, 1, -4, 0, -2, 0, 2, 1, -2, 1, 4, 1, 0, 2, 1, 2, -1, 2, -8, 0, -4, 1, 8, 1,
            -2, 2, 2, 2, -4, 2, 4, 2, 8, 2, -8, 1, -0x10, 0, -8, 2, 0x10, 1, 0x10, 2, -0x10, 1, -0x10, 2,
            0x2C, 9, 0x2D, 0x0A, 0x2E, 0x0B, 0x2F, 0x0C, 0x30, 0x0D, 0x31, 0x32, 0x0E, 0x33, 0x0F, 0x10, 0x11,
            0x12, 0x13, 0x14, 0x34, 0x15, 0x37, 0x35, 0x16, 0x38, 0x17, 0x39, 0x3D, 0x18, 0x19, 0x36, 0x1A,
            0x1B, 0x3A, 0x3C, 0x1C, 0x3B, 0x1D, 0x3E, 0x3F, 0x1E, 0x40, 0x1F, 0x45, 0x46, 0x43, 0x20, 0x21,
            0x48, 0x22, 0x41, 0x23, 0x42, 0x24, 0x25, 0x44, 0x47, 0x4F, 0x4A, 0x49, 0x53, 0x4B, 0x4D, 0x57,
            0x52, 0x59, 0x58, 0x4E, 0x50, 0x56, 0x26, 0x54, 0x4C, 0x51, 0x55, 0x27, 0x28, 0x5A, 0x2B, 0x29,
            0x5B, 0x2A, 0x5C, 0x5D, 0x5E, 0, 0, 0, 0x12, 0, 0x13, 0, 0x14, 0, 0x15, 0, 0x16, 0, 0x17, 0, 0x18,
            0, 0x19, 0, 0x1A, 0, 0x1B, 0, 0x1C, 0, 0x1D, 0, 0x1E, 0, 0x1F, 0, 0x20, 0, 0x21, 0, 0x22, 0, 0x23,
            0, 0x24, 0, 0x25, 0, 0x26, 0, 0x27, 0, 0x28, 0, 0x29, 0, 0x2A, 0, 0x2B, 0, 0x2C, 0, 0x2D, 0, 0x2E,
            0, 0x2F, 0, 0x30, 0, 0x31, 0, 0x32, 0, 0x33, 0, 0x34, 0, 0x35, 0, 0x36, 0, 0x37, 0, 0x38, 0, 0x39,
            0, 0x3A, 0, 0x3B, 0, 0x3C, 0, 0x3D, 0, 0x3E, 0, 0x3F, 0, 0x40, 0, 0x80, 0, 0x0C0, 0, 0x100, 0,
            0x140, 0x60, 0x61, 0, 0x0D, 0, 0x0E,
        };
    }
}
