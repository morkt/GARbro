//! \file       ImageTCZ.cs
//! \date       2023 Sep 28
//! \brief      Ponytail NMI 2.5 image format (PC-98).
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

// Graphics driver version 0.3  95/10/30
// by Y.Nakamura / NARIMI.A

namespace GameRes.Formats.Ponytail
{
    [Export(typeof(ImageFormat))]
    public class TczFormat : ImageFormat
    {
        public override string         Tag => "TCZ";
        public override string Description => "Ponytail Soft NMI image format";
        public override uint     Signature => 0x20494D4E; // 'NMI '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual (4, "2.5\0"))
                return null;
            var info = new ImageMetaData {
                Width  = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                OffsetX = header.ToInt16 (0x8),
                OffsetY = header.ToInt16 (0xA),
                BPP = 4,
            };
            if (info.Height > TczReader.MaxHeight)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new TczReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TczFormat.Write not implemented");
        }
    }

    internal class TczReader : TszReader
    {
        internal const int  MaxHeight = 0x190;
        const int           BufferSize = MaxHeight * 0x10;

        public TczReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = m_info.iWidth >> 1;
        }

        byte[] m_buffer;

        static short[] s_offTable0 = { -4, -3, -2, -1, 0, 1, 2, 3 };
        static short[] s_offTable1 = { -16, -8, -6, -4, -3, -2, -1, 0, 1, 2, 3, 4, 6, 8, 10, 16 };

        public new ImageData Unpack ()
        {
            m_input.Position = 0x10;
            var palette = ReadPalette();
            var output = new byte[m_stride * m_info.iHeight];
            FillBuffers();
            ResetBitReader();
            int dst = 2;
            int output_pos = 0;
            int x = 0;
            while (x < m_stride)
            {
                int y = 0;
                while (y < m_info.iHeight)
                {
                    if (GetNextBit()) // @1@
                    {
                        int src = dst;
                        if (!GetNextBit()) // @2@
                        {
                            src += m_lineOffsets1[1];
                            int off = GetBits (4);
                            src += s_offTable1[off];
                        }
                        else if (!GetNextBit()) // @3@
                        {
                            src += GetBits (2) - 4;
                        }
                        else
                        {
                            if (!GetNextBit()) // @4@
                            {
                                src += m_lineOffsets1[2];
                            }
                            else if (!GetNextBit()) // @5@
                            {
                                src += m_lineOffsets1[4];
                            }
                            else
                            {
                                src += m_lineOffsets1[8];
                            }
                            int off = GetBits (3);
                            src += s_offTable0[off];
                        }
                        src &= 0xFFFF;
                        int count = GetBitLength() + 1;
                        Binary.CopyOverlapped (m_buffer, src, dst, count);
                        y += count;
                        dst += count;
                    }
                    else
                    {
                        int bx = m_buffer[dst-2];
                        bx = (bx << 8 | bx) & 0xF00F;
                        if (GetNextBit()) // @8@
                        {
                            int ax = GetBits (4);
                            bx = (bx & 0xFF) | ax << 12;
                        }
                        if (GetNextBit()) // @9@
                        {
                            int ax = GetBits (4);
                            bx = (bx & 0xFF00) | ax;
                        }
                        bx = (bx & 0xFF) | (bx >> 8);
                        m_buffer[dst++] = (byte)bx;
                        ++y;
                    }
                }
                ++x;
                if ((x & 3) == 0)
                {
                    CopyScanline (m_lineOffsets0[(x - 1) & 0xC], output, output_pos);
                    output_pos += 4;
                }
                int z = x & 0xF;
                dst = m_lineOffsets0[z];
                if (z != 0)
                {
                    m_lineOffsets1[z] -= BufferSize;
                }
                else
                {
                    for (int i = 1; i < 16; ++i)
                    {
                        m_lineOffsets1[i] += BufferSize;
                    }
                }
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, output, m_stride);
        }

        ushort[] m_lineOffsets0 = new ushort[16];
        ushort[] m_lineOffsets1 = new ushort[16];

        void FillBuffers ()
        {
            m_buffer = new byte[BufferSize + MaxHeight];
            m_buffer[0] = m_buffer[1] = 3;
            ushort p = 2;
            for (int i = 0; i < 16; ++i)
            {
                m_lineOffsets1[i] = (ushort)(((16 - i) & 0xF) * MaxHeight);
                m_lineOffsets0[i] = p;
                p += MaxHeight;
            }
        }

        void CopyScanline (int src, byte[] output, int dst)
        {
            for (int i = 0; i < m_info.iHeight; ++i)
            {
                output[dst  ] = m_buffer[src + i ];
                output[dst+1] = m_buffer[src + i + MaxHeight  ];
                output[dst+2] = m_buffer[src + i + MaxHeight*2];
                output[dst+3] = m_buffer[src + i + MaxHeight*3];
                dst += m_stride;
            }
        }
    }
}
