//! \file       ImageFD.cs
//! \date       2019 Mar 27
//! \brief      Mink compressed bitmap format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Mink
{
    [Export(typeof(ImageFormat))]
    public class FdFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/FD"; } }
        public override string Description { get { return "Mink compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public FdFormat ()
        {
            Signatures = new uint[] { 0x00186446, 0x00184446, 0x00206446, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (header[0] != 'F' || (header[1] & 0x5F) != 'D' || header[3] > 1)
                return null;
            int bpp = header[2];
            if (bpp != 24 && bpp != 32)
                return null;
            return new FcMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = bpp,
                Flag   = header[3],
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new FdReader (file, (FcMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FdFormat.Write not implemented");
        }
    }

    internal class FdReader
    {
        IBinaryStream   m_input;
        FcMetaData      m_info;

        public FdReader (IBinaryStream input, FcMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 16;
            var output = new uint[m_info.iWidth * m_info.iHeight];
            UnpackRgb (output);
            if (32 == m_info.BPP)
                UnpackAlpha (output);
            PixelFormat format = 32 == m_info.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.CreateFlipped (m_info, format, null, output, m_info.iWidth * 4);
        }

        byte m_cur_bits;

        void UnpackRgb (uint[] output)
        {
            InitOffsetTable();
            int dst = 0;
            m_cur_bits = 0x80;
            while (dst < output.Length)
            {
                int ctl = ReadNext();
                int code = ControlTable[ctl - 2];
                if (code < 20)
                {
                    if (code < 2)
                    {
                        uint pixel = (uint)m_input.ReadUInt8() << 24;
                        pixel += (uint)m_input.ReadUInt8() << 16;
                        pixel += (uint)m_input.ReadUInt8() << 8;
                        pixel ^= 0x80000080;
                        pixel >>= FlowMap5[m_cur_bits];
                        output[dst++] = (pixel >> 8) ^ ((uint)m_cur_bits << 16);
                        m_cur_bits = (byte)pixel;
                    }
                    else
                    {
                        int pixel = (int)output[dst + m_offset_table[code - 2]];
                        int r = ReadNext();
                        pixel += (((r - 2) << 15) ^ -((r & 1) << 15));

                        int g = ReadNext();
                        pixel += (((g - 2) << 7) ^ -((g & 1) << 7));

                        int b = ReadNext();
                        output[dst++] = (uint)((((b - 2) ^ -(b & 1)) >> 1) + pixel);
                    }
                }
                else // code >= 20
                {
                    int y = ReadNext();
                    int x = ReadNext();
                    int src = dst + (((y & 1) - 1) ^ (x - 2)) - m_info.iWidth * ((y >> 1) + (y & 1) - 1);
                    int count = ReadNext() - 1;
                    if (code == 38)
                    {
                        while (count --> 0)
                        {
                            output[dst++] = output[src++];
                        }
                    }
                    else
                    {
                        int offset = m_offset_table[code - 20]; // dword_5D51F8[code];
                        while (count --> 0)
                        {
                            uint pixel = output[src] + output[dst + offset] - output[src + offset];
                            output[dst++] = pixel;
                            ++src;
                        }
                    }
                }

            }
        }

        void UnpackAlpha (uint[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int shift = 8 - FlowMap5[m_cur_bits];
                uint ctl, alpha;
                if (shift <= 0)
                {
                    alpha = m_cur_bits;
                    ctl = 0;
                }
                else
                {
                    ctl = (uint)m_cur_bits >> shift;
                    if (shift > 8)
                    {
                        int v94 = ((shift - 9) >> 3) + 1;
                        do
                        {
                            ctl <<= 8;
                            ctl += m_input.ReadUInt8();
                            shift -= 8;
                            --v94;
                        }
                        while (v94 > 0);
                    }
                    m_cur_bits = m_input.ReadUInt8();
                    alpha = (ctl << shift) + ((uint)m_cur_bits >> (8 - shift));
                    ctl &= ~0xFFu;
                    ctl |= ((uint)(m_cur_bits << shift) + (1u << (shift - 1))) & 0xFFu;
                }
                m_cur_bits = (byte)ctl;
                ctl &= 0xFFu;
                int v96 = FlowMap3[m_cur_bits];
                m_cur_bits = FlowMap1[m_cur_bits];
                if (0 == m_cur_bits)
                {
                    do
                    {
                        m_cur_bits = m_input.ReadUInt8();
                        v96 += FlowMap4[m_cur_bits];
                    }
                    while (0 == FlowMap2[m_cur_bits]);
                    m_cur_bits = FlowMap2[m_cur_bits];
                }
                int v98 = FlowMap5[m_cur_bits];
                int count, bits;
                if (v96 <= v98)
                {
                    count = (m_cur_bits + 256) >> (8 - v96);
                    bits = m_cur_bits << v96;
                }
                else // v96 > v98
                {
                    int v99 = v96 - v98;
                    int v100 = (m_cur_bits + 256) >> (8 - v98);
                    if (v99 > 8)
                    {
                        int v101 = (int)(((uint)(v99 - 9) >> 3) + 1);
                        v99 -= v101 << 3;
                        do
                        {
                            v100 = m_input.ReadUInt8() + (v100 << 8);
                            --v101;
                        }
                        while (v101 > 0);
                    }
                    m_cur_bits = m_input.ReadUInt8();
                    count = (int)((v100 << v99) + ((uint)m_cur_bits >> (8 - v99)));
                    bits = (m_cur_bits << v99) + (1 << (v99 - 1));
                }
                m_cur_bits = (byte)bits;
                count = Math.Min (count - 1, output.Length - dst);
                alpha <<= 24;
                while (count --> 0)
                {
                    uint px = output[dst] & 0xFFFFFFu;
                    output[dst++] = px | alpha;
                }
            }
        }

        int ReadNext ()
        {
            int shift = FlowMap3[m_cur_bits];
            m_cur_bits = FlowMap1[m_cur_bits];
            if (0 == m_cur_bits)
            {
                do
                {
                    m_cur_bits = m_input.ReadUInt8();
                    shift += FlowMap4[m_cur_bits];
                }
                while (0 == FlowMap2[m_cur_bits]);
                m_cur_bits = FlowMap2[m_cur_bits];
            }
            int bits = m_cur_bits + 256;
            int v12 = FlowMap5[m_cur_bits];
            if (shift > v12)
            {
                bits <<= v12;
                bits &= ~0xFF;
                bits += m_input.ReadUInt8();
                shift -= v12 + 1;
                if (shift >= 8)
                {
                    int count = shift >> 3;
                    shift -= count << 3;
                    do
                    {
                        bits += m_input.ReadUInt8();
                        bits <<= 8;
                        --count;
                    }
                    while (count > 0);
                }
                bits = bits << 1 | 1;
                shift &= 0xFF;
            }
            bits <<= shift;
            m_cur_bits = (byte)bits;
            return bits >> 8;
        }

        int[] m_offset_table = new int[18];

        static readonly sbyte[] OffsetsX = { -1,  0,  1, -1,  0, -2,  0, -3,  0, -4,  0, -5,  0, -6,  0, -7,  0, -8 };
        static readonly sbyte[] OffsetsY = {  0, -1, -1, -1, -2,  0, -3,  0, -4,  0, -5,  0, -6,  0, -7,  0, -8,  0 };

        static readonly byte[] ControlTable = new byte[38];
        static readonly byte[] ControlMap = new byte[] {
            2, 1, 0, 3, 4, 5, 10, 11, 12, 13, 14, 15, 22, 23, 24, 25, 26, 27, 28, 29,
            6, 8, 7, 9, 16, 17, 18, 19, 20, 21, 30, 31, 32, 33, 34, 35, 36, 37
        };

        void InitOffsetTable ()
        {
            for (int i = 0; i < 18; ++i)
            {
                m_offset_table[i] = OffsetsX[i] + m_info.iWidth * OffsetsY[i];
            }
        }

        static readonly byte[] FlowMap1 = new byte[256];
        static readonly byte[] FlowMap2 = new byte[256];
        static readonly byte[] FlowMap3 = new byte[256];
        static readonly byte[] FlowMap4 = new byte[256];
        static readonly byte[] FlowMap5 = new byte[256];

        static FdReader ()
        {
            for (int i = 0; i < 38; ++i)
            {
                if (1 == i)
                    ControlTable[1] = 38;
                else
                    ControlTable[ControlMap[i]] = (byte)i;
            }
            for (int i = 0; i < 256; ++i)
            {
                sbyte v = (sbyte)i;
                byte n = 0;
                if (i > 0)
                {
                    while (v < 0)
                    {
                        v <<= 1;
                        ++n;
                    }
                    v <<= 1;
                    if (0 == v)
                        --n;
                }
                FlowMap1[i] = (byte)v;
                FlowMap3[i] = ++n;

                v = (sbyte)i;
                n = 0;
                if (i > 0)
                {
                    while (v < 0)
                    {
                        v <<= 1;
                        ++n;
                    }
                    v <<= 1;
                }
                FlowMap4[i] = n;
                FlowMap2[i] = (byte)(v + (1 << n));

                v = (sbyte)i;
                n = 0;
                while ((v & 0x7F) != 0)
                {
                    v <<= 1;
                    ++n;
                }
                FlowMap5[i] = n;
            }
        }
    }
}
