//! \file       ImageBMP.cs
//! \date       Tue Nov 10 10:31:23 2015
//! \brief      μ-GameOperationSystem compressed bitmap.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.uGOS
{
    internal class DetBmpMetaData : ImageMetaData
    {
        public int  Method;
    }

    [Export(typeof(ImageFormat))]
    public class DetBmpFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/uGOS"; } }
        public override string Description { get { return "μ-GameOperationSystem compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public DetBmpFormat ()
        {
            Extensions = new string[] { "bmp" };
            Signatures = new uint[] { 0x206546, 0x186546, 0x086546, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            if (header[0] != 'F')
                return null;
            var type = header[1] & 0x5F;
            if (type != 'E')
                return null;
            int bpp = header[2];
            if (bpp != 8 && bpp != 0x18 && bpp != 0x20)
                return null;
            return new DetBmpMetaData
            {
                Width   = header.ToUInt16 (4),
                Height  = header.ToUInt16 (6),
                BPP     = bpp,
                Method  = type,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Reader (file, (DetBmpMetaData)info);
            reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, null, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DetBmpFormat.Write not implemented");
        }

        internal sealed class Reader
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_bpp;

            public PixelFormat Format { get; private set; }
            public byte[]        Data { get { return m_output; } }
            public int         Stride { get; private set; }

            public Reader (IBinaryStream input, DetBmpMetaData info)
            {
                m_input = input;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_output = new byte[m_width*m_height*4];

                Format = 32 == m_bpp ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                Stride = m_width * 4;
                InitTable0();
            }

            static readonly sbyte[] OffsetsX = {
                -1,  0,  1, -1, -2, -2, -2,
                -1,  0,  1,  2,  2, -3, -3, -3, -3, -2,
                -1,  0,  1,  2,  3,  3,  3, -4, -4, -4, -4, -4, -3, -2,
                -1,  0,  1,  2,  3,  4,  4,  4,  4
            };

            static readonly sbyte[] OffsetsY = {
                 0, -1, -1, -1,
                 0, -1, -2, -2, -2, -2, -2, -1,
                 0, -1, -2, -3, -3, -3, -3, -3, -3, -3, -2, -1,
                 0, -1, -2, -3, -4, -4, -4, -4, -4, -4, -4, -4, -4, -3, -2, -1
            };
            
            byte[] byte_4CAD28 = new byte[512];
            byte[] byte_4CAF28 = new byte[512];

            int[] dword_4CB128 = new int[163];
            int[] dword_4CB750 = new int[40];
            int[] dword_4CB7F0 = new int[163];
            byte[] byte_4CBA80 = new byte[256];

            uint m_bits;

            public void Unpack ()
            {
                m_input.Position = 0x10;
                InitTable1();
                InitTable2();
                InitTable3();
                m_bits = 0x80;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int v32 = ReadNext();
                    int v33 = v32 - 2;
                    int v34 = dword_4CB7F0[v33];
                    int v35 = dword_4CB128[v34];
                    int v36 = v33;
                    int v37 = Math.Max (v32 - 4, 0);
                    for (int v39 = v33 - v37; v39 > 0; --v39)
                    {
                        dword_4CB7F0[v36] = dword_4CB7F0[v36-1];
                        --v36;
                    }
                    dword_4CB7F0[v36] = v34;
                    if (2 == v35)
                    {
                        int v49 = ReadNext();
                        int v57 = ReadNext();
                        int offset = (((v49 & 1) - 1) ^ (v57 - 2)) - m_width * ((v49 & 1) - 1 + v49 / 2);
                        Buffer.BlockCopy (m_output, dst+4*offset, m_output, dst, 4);
                        dst += 4;
                        continue;
                    }
                    if (v35 >= 43)
                    {
                        int v100;
                        if (v35 >= 123)
                            v100 = LittleEndian.ToInt32 (m_output, dst - 4 * m_width)
                                + LittleEndian.ToInt32 (m_output, dst + 4 * dword_4CB750[v35-123])
                                - LittleEndian.ToInt32 (m_output, dst + 4 * (dword_4CB750[v35-123] - m_width));
                        else if (v35 >= 83)
                            v100 = LittleEndian.ToInt32 (m_output, dst + 4 * dword_4CB750[v35-83])
                                + LittleEndian.ToInt32 (m_output, dst - 4)
                                - LittleEndian.ToInt32 (m_output, dst + 4 * dword_4CB750[v35-83] - 4);
                        else
                            v100 = LittleEndian.ToInt32 (m_output, dst + 4 * dword_4CB750[v35-43]);

                        int v57 = ReadNext();
                        int v113 = (v57 - 2) ^ -(v57 & 1);
                        v100 += (v113 << 15);

                        v57 = ReadNext();
                        v113 = (v57 - 2) ^ -(v57 & 1);
                        v100 += (v113 << 7);

                        v57 = ReadNext();
                        v113 = (v57 - 2) ^ -(v57 & 1);
                        v113 -= v113 >> 31; // cdq; sub eax, edx
                        LittleEndian.Pack (v100 + (v113 >> 1), m_output, dst);
                        dst += 4;
                        continue;
                    }
                    if (0 == v35)
                    {
//                        v60 = (((src1[2] + ((src1[1] + (*src1 << 8)) << 8)) << 8) ^ 0x80000080u) >> byte_4CBA80[v38];
                        uint v60 = (uint)m_input.ReadUInt8() << 16;
                        uint v38 = m_bits & 0xFF;
                        v60 |= (uint)m_input.ReadUInt8 () << 8;
                        v60 |= m_input.ReadUInt8 ();
                        v60 <<= 8;
                        v60 = (v60 ^ 0x80000080u) >> byte_4CBA80[v38];
                        m_bits = v60;
                        LittleEndian.Pack ((v38 << 16) ^ (v60 >> 8), m_output, dst);
                        dst += 4;
                        continue;
                    }
                    int v70 = ReadNext();
                    int v77 = ReadNext();
                    int v78 = (((v70 & 1) - 1) ^ (v77 - 2)) - m_width * ((v70 & 1) - 1 + (v70 >> 1));
                    int src2 = dst + 4 * v78;
                    if (1 == v35)
                    {
                        int count = ReadNext() * 4;
                        Binary.CopyOverlapped (m_output, src2, dst, count);
                        dst += count;
                        continue;
                    }
                    int d;
                    if (0x80 == (m_bits & 0xFF))
                    {
                        byte n = m_input.ReadUInt8();
                        d = n >> 7;
                        m_bits = (uint)(n << 1 | 1);
                    }
                    else
                    {
                        d = (int)(m_bits & 0xFF) >> 7;
                        m_bits <<= 1;
                    }
                    int v90 = dword_4CB750[v35-3];
                    int a = LittleEndian.ToInt32 (m_output, src2);
                    int b = LittleEndian.ToInt32 (m_output, dst + 4 * v90);
                    int c = LittleEndian.ToInt32 (m_output, src2 + 4 * v90);
                    int v95 = (a & 0xFF00FF) + (b & 0xFF00FF) - (c & 0xFF00FF);
                    int v96 = (v95 & 0xFF00FF) + (((a & 0xFF00) + (b & 0xFF00) - (c & 0xFF00)) & 0xFF00);
                    LittleEndian.Pack (v96, m_output, dst);
                    dst += 4;
                    if (d != 0)
                    {
                        a = LittleEndian.ToInt32 (m_output, src2 + 4);
                        b = LittleEndian.ToInt32 (m_output, dst + 4 * v90);
                        c = LittleEndian.ToInt32 (m_output, src2 + 4 * v90 + 4);
                        v95 = (a & 0xFF00FF) + (b & 0xFF00FF) - (c & 0xFF00FF);
                        v96 = (v95 & 0xFF00FF) + (((a & 0xFF00) + (b & 0xFF00) - (c & 0xFF00)) & 0xFF00);
                        LittleEndian.Pack (v96, m_output, dst);
                        dst += 4;
                    }
                }
                if (32 == m_bpp)
                {
                    dst = 3;
                    while (dst < m_output.Length)
                    {
                        byte alpha = ReadBits (8);
                        int count = ReadCount() + 1;
                        while (count --> 0)
                        {
                            m_output[dst] = alpha;
                            dst += 4;
                        }
                    }
                }
                else if (8 == m_bpp)
                {
                    var pixels = new byte[m_width * m_height];
                    dst = 0;
                    for (int src = 0; src < m_output.Length; src += 4)
                    {
                        pixels[dst++] = m_output[src];
                    }
                    m_output = pixels;
                    Format = PixelFormats.Gray8;
                    Stride = m_width;
                }
            }

            int ReadNext ()
            {
                m_bits &= 0xFF;
                int v26 = byte_4CAD28[2 * m_bits];
                int v23 = byte_4CAD28[2 * m_bits + 1];
                while (0 == v23)
                {
                    int b = m_input.ReadUInt8();
                    v26 += byte_4CAF28[2 * b];
                    v23  = byte_4CAF28[2 * b + 1];
                }
                int v27 = byte_4CBA80[v23];
                int v28 = v23 + 256;
                if (v26 - v27 > 0)
                {
                    uint v29 = (uint)(v26 - v27 - 1);
                    int v30 = m_input.ReadUInt8() + (int)((v28 << v27) & 0xFFFFFF00);
                    if ((int)v29 >= 8)
                    {
                        for (uint v31 = v29 >> 3; v31 != 0; --v31)
                        {
                            v30 = (v30 << 8) | m_input.ReadUInt8();
                        }
                        v29 -= 8 * (v29 >> 3);
                    }
                    v28 = v30 << 1 | 1;
                    v26 = (int)v29;
                }
                m_bits = (uint)(v28 << v26);
                return (int)(m_bits >> 8);
            }

            byte ReadBits (int a3)
            {
                m_bits &= 0xFF;
                int v4 = byte_4CBA80[m_bits];
                int v5 = a3 - v4;
                uint alpha; // eax@5
                if (v5 <= 0)
                {
                    alpha = m_bits >> (8 - a3);
                    m_bits = (m_bits << a3) & 0xFF;
                }
                else
                {
                    int v6 = (int)(m_bits >> (8 - v4));
                    if (v5 > 8)
                    {
                        int count = ((v5 - 9) >> 3) + 1;
                        while (count --> 0)
                        {
                            v6 = m_input.ReadUInt8() + (v6 << 8);
                            v5 -= 8;
                        }
                    }
                    m_bits = m_input.ReadUInt8();
                    alpha = (uint)((v6 << v5) + (m_bits >> (8 - v5)));
                    m_bits = ((m_bits << v5) + (1u << (v5 - 1))) & 0xFFu;
                }
                return (byte)alpha;
            }

            int ReadCount ()
            {
                m_bits &= 0xFF;
                int v2 = (int)m_bits;
                int i = byte_4CAD28[2 * v2];
                uint v4 = byte_4CAD28[2 * v2 + 1];
                while (0 == v4)
                {
                    v2 = m_input.ReadUInt8();
                    i += byte_4CAF28[2 * v2];
                    v4 = byte_4CAF28[2 * v2 + 1];
                }
                m_bits = v4;
                int v5 = byte_4CBA80[v4];
                if (i - v5 <= 0)
                {
                    m_bits = v4 << i;
                    return (int)((uint)(v4 + 256) >> (8 - i)) - 2;
                }
                else
                {
                    int v6 = i - v5;
                    uint v7 = (uint)(v4 + 256) >> (8 - v5);
                    if (v6 > 8)
                    {
                        for (uint v8 = ((uint)(v6 - 9) >> 3) + 1; v8 != 0; --v8)
                        {
                            v7 = (v7 << 8) | m_input.ReadUInt8();
                        }
                        v6 += -8 * (int)(((uint)(v6 - 9) >> 3) + 1);
                    }
                    m_bits = m_input.ReadUInt8();
                    uint n = (v7 << v6) + (m_bits >> (8 - v6));
                    m_bits = (m_bits << v6) + (1u << (v6 - 1));
                    return (int)n - 2;
                }
            }

            void InitTable0 () //sub_4153B0()
            {
                for (int i = 0; i < 256; ++i)
                {
                    byte v1 = 0;
                    sbyte v2 = (sbyte)i;
                    if (i != 0)
                    {
                        while (v2 < 0)
                        {
                            v2 <<= 1;
                            ++v1;
                        }
                        v2 <<= 1;
                        if (0 == v2)
                            --v1;
                    }
                    byte_4CAD28[2 * i] = (byte)(v1 + 1);
                    byte_4CAD28[2 * i + 1] = (byte)v2;
                    byte v4 = 0;
                    sbyte v3 = (sbyte)i;
                    if (i != 0)
                    {
                        while (v3 < 0)
                        {
                            v3 <<= 1;
                            ++v4;
                        }
                        v3 <<= 1;
                    }
                    byte_4CAF28[2 * i] = v4;
                    byte_4CAF28[2 * i + 1] = (byte)(v3 + (1 << v4));
                    byte v5 = 0;
                    for (int j = i; 0 != (j & 0x7F); j <<= 1)
                        ++v5;
                    byte_4CBA80[i] = v5;
                }
            }

            void InitTable1 () // init_table_415FF0()
            {
                int[] dword_4CB3B8 = new int[163];
                dword_4CB3B8[0] = 0;
                dword_4CB3B8[1] = 1;
                dword_4CB3B8[2] = 2;
                int v1 = 44;
                for (int i = 3; i < 43; ++i)
                {
                    dword_4CB3B8[i] = i + 120;
                    dword_4CB3B8[i+40] = i;
                    dword_4CB3B8[i+80] = v1-1;
                    dword_4CB3B8[i+120] = v1;
                    v1 += 2;
                }
                for (int i = 0; i < 163; ++i)
                {
                    dword_4CB128[dword_4CB3B8[i]] = i;
                }
            }

            void InitTable2 () // init_table_416070()
            {
                for (int i = 0; i < 163; ++i)
                {
                    dword_4CB7F0[i] = i;
                }
            }

            void InitTable3 ()
            {
                for (int i = 0; i < 40; ++i)
                {
                    dword_4CB750[i] = OffsetsX[i] + m_width * OffsetsY[i];
                }
            }
        }
    }
}
