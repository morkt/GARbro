//! \file       Bc7Decoder.cs
//! \date       2022 May 03
//! \brief      BC7 texture compression decoder.
//
// Based on the [bc7enc](https://github.com/richgel999/bc7enc)
//
// Copyright(c) 2020 Richard Geldreich, Jr.
//
// C# port copyright (C) 2022 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of
// this software and associated documentation files(the "Software"), to deal in
// the Software without restriction, including without limitation the rights to
// use, copy, modify, merge, publish, distribute, sublicense, and / or sell copies
// of the Software, and to permit persons to whom the Software is furnished to do
// so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using GameRes.Utility;

namespace GameRes.Formats.Unity
{
    public class Bc7Decoder
    {
        byte[]  m_input;
        int     m_width;
        int     m_height;
        int     m_output_stride;
        byte[]  m_output;
        byte[]  m_block = new byte[64];

        public Bc7Decoder (byte[] input, ImageMetaData info)
        {
            m_input = input;
            m_width = info.iWidth;
            m_output_stride = m_width * 4;
            m_height = info.iHeight;
            m_output = new byte[m_output_stride*m_height];
        }

        public byte[] Unpack ()
        {
            int block_step_y = m_output_stride * 4;
            int block_step_x = 16;
            int src = 0;
            for (int y = 0; y < m_output.Length; y += block_step_y)
            for (int x = 0; x < m_output_stride; x += block_step_x)
            {
                DecompressBc7Block (src);
                int dst = y + x;
                int block_src = 0;
                for (int i = 0; i < 4; ++i)
                {
                    int row_dst = dst;
                    for (int j = 0; j < 4; ++j)
                    {
                        m_output[row_dst++] = m_block[block_src+2];
                        m_output[row_dst++] = m_block[block_src+1];
                        m_output[row_dst++] = m_block[block_src  ];
                        m_output[row_dst++] = m_block[block_src+3];
                        block_src += 4;
                    }
                    dst += m_output_stride;
                }
                src += 16;
            }
            return m_output;
        }

        private bool DecompressBc7Block (int src)
        {
            byte first_byte = m_input[src];
            for (int mode = 0; mode < 8; ++mode)
            {
                if ((first_byte & (1u << mode)) != 0)
                {
                    switch (mode)
                    {
                    case 0:
                    case 2:
                        return UnpackBc7Mode0_2 (mode, src);
                    case 1:
                    case 3:
                    case 7:
                        return UnpackBc7Mode1_3_7 (mode, src);
                    case 4:
                    case 5:
                        return UnpackBc7Mode4_5 (mode, src);
                    case 6:
                        return UnpackBc7Mode6 (src);
                    }
                }
            }
            return false;
        }

        int m_bit_offset;

        uint ReadBits32 (int src, int codesize)
        {
            uint bits = 0;
            int total_bits = 0;

            while (total_bits < codesize)
            {
                int byte_bit_offset = m_bit_offset & 7;
                int bits_to_read = Math.Min (codesize - total_bits, 8 - byte_bit_offset);

                uint byte_bits = (uint)m_input[src + (m_bit_offset >> 3)] >> byte_bit_offset;
                byte_bits &= ((1u << bits_to_read) - 1u);

                bits |= (byte_bits << total_bits);

                total_bits += bits_to_read;
                m_bit_offset += bits_to_read;
            }
            return bits;
        }

        byte[] endpoints = new byte[8 * 4];
        uint[] pbits = new uint[6];
        uint[] weights = new uint[16];
        uint[] a_weights = new uint[16];
        int[] weight_bits = new int[2];
        byte[,] block_colors= new byte[3,32];

        bool UnpackBc7Mode0_2 (int mode, int src)
        {
            const uint ENDPOINTS = 6;
            const uint COMPS = 3;
            int WEIGHT_BITS = (mode == 0) ? 3 : 2;
            int ENDPOINT_BITS = (mode == 0) ? 4 : 5;
            int PBITS = (mode == 0) ? 6 : 0;
            uint WEIGHT_VALS = 1u << WEIGHT_BITS;
                
            m_bit_offset = 0;

            if (ReadBits32 (src, mode + 1) != (1u << mode))
                return false;

            uint part = ReadBits32 (src, (mode == 0) ? 4 : 6);

            for (uint c = 0; c < COMPS; c++)
                for (uint e = 0; e < ENDPOINTS * 4; e += 4)
                    endpoints[e+c] = (byte)ReadBits32(src, ENDPOINT_BITS);

            for (uint p = 0; p < PBITS; p++)
                pbits[p] = ReadBits32 (src, 1);

            for (uint i = 0; i < 16; i++)
                weights[i] = ReadBits32 (src, ((i == 0) || (i == s_bc7_table_anchor_index_third_subset_1[part]) || (i == s_bc7_table_anchor_index_third_subset_2[part])) ? (WEIGHT_BITS - 1) : WEIGHT_BITS);

            for (uint e = 0; e < ENDPOINTS * 4; e += 4)
                for (uint c = 0; c < 4; c++)
                    endpoints[e+c] = (byte)((c == 3) ? 0xFF : (PBITS != 0 ? bc7_dequant(endpoints[e+c], pbits[e/4], ENDPOINT_BITS) : bc7_dequant(endpoints[e+c], ENDPOINT_BITS)));

            for (uint s = 0; s < 3; s++)
                for (uint i = 0; i < WEIGHT_VALS*4; i += 4)
                {
                    for (uint c = 0; c < 3; c++)
                        block_colors[s,i+c] = (byte)bc7_interp(endpoints[s * 8 + c], endpoints[s * 8 + 4 + c], i/4, WEIGHT_BITS);
                    block_colors[s,i+3] = 0xFF;
                }

            for (uint i = 0; i < 16*4; i += 4)
            {
                int b = s_bc7_partition3[part * 16 + i/4];
                uint c = weights[i/4] * 4;
                m_block[i  ]   = block_colors[b,c];
                m_block[i+1]   = block_colors[b,c+1];
                m_block[i+2]   = block_colors[b,c+2];
                m_block[i+3]   = block_colors[b,c+3];
            }
            return true;
        }

        bool UnpackBc7Mode1_3_7 (int mode, int src)
        {
            const uint ENDPOINTS = 4;
            int COMPS = (mode == 7) ? 4 : 3;
            int WEIGHT_BITS = (mode == 1) ? 3 : 2;
            int ENDPOINT_BITS = (mode == 7) ? 5 : ((mode == 1) ? 6 : 7);
            int PBITS = (mode == 1) ? 2 : 4;
            bool SHARED_PBITS = mode == 1;
            uint WEIGHT_VALS = 1u << WEIGHT_BITS;
                
            m_bit_offset = 0;

            if (ReadBits32 (src, mode + 1) != (1u << mode))
                return false;

            uint part = ReadBits32 (src, 6);

            for (uint c = 0; c < COMPS; c++)
                for (uint e = 0; e < ENDPOINTS * 4; e += 4)
                    endpoints[e+c] = (byte)ReadBits32(src, ENDPOINT_BITS);
                
            for (uint p = 0; p < PBITS; p++)
                pbits[p] = ReadBits32 (src, 1);
                                
            for (uint i = 0; i < 16; i++)
                weights[i] = ReadBits32(src, ((i == 0) || (i == s_bc7_table_anchor_index_second_subset[part])) ? (WEIGHT_BITS - 1) : WEIGHT_BITS);

            for (uint e = 0; e < ENDPOINTS*4; e += 4)
                for (uint c = 0; c < 4; c++)
                    endpoints[e+c] = (byte)((c == ((mode == 7u) ? 4u : 3u)) ? 0xFF : bc7_dequant(endpoints[e+c], pbits[SHARED_PBITS ? ((e/4) >> 1) : (e/4)], ENDPOINT_BITS));
                
            for (uint s = 0; s < 2; s++)
                for (uint i = 0; i < WEIGHT_VALS*4; i += 4)
                {
                    for (uint c = 0; c < COMPS; c++)
                        block_colors[s,i+c] = (byte)bc7_interp(endpoints[(s * 2)*4+c], endpoints[(s * 2 + 1)*4+c], i/4, WEIGHT_BITS);
                    block_colors[s,i+3] = (COMPS == 3) ? (byte)0xFF : block_colors[s,i+3];
                }

            for (uint i = 0; i < 16*4; i += 4)
            {
                int b = s_bc7_partition2[part * 16 + i/4];
                uint c = weights[i/4] * 4;
                m_block[i  ] = block_colors[b,c];
                m_block[i+1] = block_colors[b,c+1];
                m_block[i+2] = block_colors[b,c+2];
                m_block[i+3] = block_colors[b,c+3];
            }
            return true;
        }

        bool UnpackBc7Mode4_5 (int mode, int src)
        {
            const uint ENDPOINTS = 2;
            const uint COMPS = 4;
            const int WEIGHT_BITS = 2;
            int A_WEIGHT_BITS = (mode == 4) ? 3 : 2;
            int ENDPOINT_BITS = (mode == 4) ? 5 : 7;
            int A_ENDPOINT_BITS = (mode == 4) ? 6 : 8;

            m_bit_offset = 0;

            if (ReadBits32(src, mode + 1) != (1u << mode))
                return false;

            uint comp_rot = ReadBits32 (src, 2);
            uint index_mode = (mode == 4) ? ReadBits32 (src, 1) : 0;

            for (uint c = 0; c < COMPS; c++)
                for (uint e = 0; e < ENDPOINTS*4; e += 4)
                    endpoints[e+c] = (byte)ReadBits32(src, (c == 3) ? A_ENDPOINT_BITS : ENDPOINT_BITS);
                
            weight_bits[0] = index_mode != 0 ? A_WEIGHT_BITS : WEIGHT_BITS;
            weight_bits[1] = index_mode != 0 ? WEIGHT_BITS : A_WEIGHT_BITS;

            uint[] w_array = index_mode != 0 ? a_weights : weights;
            for (uint i = 0; i < 16; i++)
                w_array[i] = ReadBits32 (src, weight_bits[index_mode] - ((i == 0) ? 1 : 0));

            w_array = index_mode != 0 ? weights : a_weights;
            for (uint i = 0; i < 16; i++)
                w_array[i] = ReadBits32 (src, weight_bits[1 - index_mode] - ((i == 0) ? 1 : 0));

            for (uint e = 0; e < ENDPOINTS*4; e += 4)
                for (uint c = 0; c < 4; c++)
                    endpoints[e+c] = (byte)bc7_dequant(endpoints[e+c], (c == 3) ? A_ENDPOINT_BITS : ENDPOINT_BITS);

            for (uint i = 0; i < (1U << weight_bits[0]) * 4; i += 4)
                for (uint c = 0; c < 3; c++)
                    block_colors[0,i+c] = (byte)bc7_interp(endpoints[c], endpoints[4+c], i/4, weight_bits[0]);

            for (uint i = 0; i < (1U << weight_bits[1]) * 4; i += 4)
                block_colors[0,i+3] = (byte)bc7_interp(endpoints[3], endpoints[4+3], i/4, weight_bits[1]);

            for (uint i = 0; i < 16*4; i += 4)
            {
                uint w = weights[i / 4] * 4;
                m_block[i  ] = block_colors[0,w];
                m_block[i+1] = block_colors[0,w+1];
                m_block[i+2] = block_colors[0,w+2];
                m_block[i+3] = block_colors[0,a_weights[i/4]*4+3];
                if (comp_rot >= 1)
                {
                    byte a = m_block[i+3];
                    m_block[i+3] = m_block[i+comp_rot-1];
                    m_block[i+comp_rot-1] = a;
                }
            }
            return true;
        }

        internal class Bc7Mode_6
        {
            public struct Lo
            {
                public byte mode ; //: 7;
                public byte r0 ; //: 7;
                public byte r1 ; //: 7;
                public byte g0 ; //: 7;
                public byte g1 ; //: 7;
                public byte b0 ; //: 7;
                public byte b1 ; //: 7;
                public byte a0 ; //: 7;
                public byte a1 ; //: 7;
                public byte p0 ; //: 1;
            }
            public struct Hi
            {
                public byte p1  ; //: 1;
                public byte s00 ; //: 3;
                public byte s10 ; //: 4;
                public byte s20 ; //: 4;
                public byte s30 ; //: 4;

                public byte s01 ; //: 4;
                public byte s11 ; //: 4;
                public byte s21 ; //: 4;
                public byte s31 ; //: 4;

                public byte s02 ; //: 4;
                public byte s12 ; //: 4;
                public byte s22 ; //: 4;
                public byte s32 ; //: 4;

                public byte s03 ; //: 4;
                public byte s13 ; //: 4;
                public byte s23 ; //: 4;
                public byte s33 ; //: 4;
            }
            public Lo       m_lo;
            public Hi       m_hi;

            public void Unpack (byte[] input, int src)
            {
                ulong lo_bits = input.ToUInt64 (src);
                ulong hi_bits = input.ToUInt64 (src+8);
                m_lo.mode = (byte)( lo_bits        & 0x7F);
                m_lo.r0   = (byte)((lo_bits >>  7) & 0x7F);
                m_lo.r1   = (byte)((lo_bits >> 14) & 0x7F);
                m_lo.g0   = (byte)((lo_bits >> 21) & 0x7F);
                m_lo.g1   = (byte)((lo_bits >> 28) & 0x7F);
                m_lo.b0   = (byte)((lo_bits >> 35) & 0x7F);
                m_lo.b1   = (byte)((lo_bits >> 42) & 0x7F);
                m_lo.a0   = (byte)((lo_bits >> 49) & 0x7F);
                m_lo.a1   = (byte)((lo_bits >> 56) & 0x7F);
                m_lo.p0   = (byte)((lo_bits >> 63));

                m_hi.p1  = (byte)((hi_bits & 1));
                m_hi.s00 = (byte)((hi_bits >>  1) & 0x7);
                m_hi.s10 = (byte)((hi_bits >>  4) & 0xF);
                m_hi.s20 = (byte)((hi_bits >>  8) & 0xF);
                m_hi.s30 = (byte)((hi_bits >> 12) & 0xF);
                m_hi.s01 = (byte)((hi_bits >> 16) & 0xF);
                m_hi.s11 = (byte)((hi_bits >> 20) & 0xF);
                m_hi.s21 = (byte)((hi_bits >> 24) & 0xF);
                m_hi.s31 = (byte)((hi_bits >> 28) & 0xF);
                m_hi.s02 = (byte)((hi_bits >> 32) & 0xF);
                m_hi.s12 = (byte)((hi_bits >> 36) & 0xF);
                m_hi.s22 = (byte)((hi_bits >> 40) & 0xF);
                m_hi.s32 = (byte)((hi_bits >> 44) & 0xF);
                m_hi.s03 = (byte)((hi_bits >> 48) & 0xF);
                m_hi.s13 = (byte)((hi_bits >> 52) & 0xF);
                m_hi.s23 = (byte)((hi_bits >> 56) & 0xF);
                m_hi.s33 = (byte)((hi_bits >> 60));
            }
        }

        Bc7Mode_6 mode_6_block = new Bc7Mode_6();
        uint[] vals = new uint[16];

        bool UnpackBc7Mode6(int src)
        {
            mode_6_block.Unpack (m_input, src);
            var block = mode_6_block;

            if (block.m_lo.mode != (1 << 6))
                return false;

            uint r0 = (uint)((block.m_lo.r0 << 1) | block.m_lo.p0);
            uint g0 = (uint)((block.m_lo.g0 << 1) | block.m_lo.p0);
            uint b0 = (uint)((block.m_lo.b0 << 1) | block.m_lo.p0);
            uint a0 = (uint)((block.m_lo.a0 << 1) | block.m_lo.p0);
            uint r1 = (uint)((block.m_lo.r1 << 1) | block.m_hi.p1);
            uint g1 = (uint)((block.m_lo.g1 << 1) | block.m_hi.p1);
            uint b1 = (uint)((block.m_lo.b1 << 1) | block.m_hi.p1);
            uint a1 = (uint)((block.m_lo.a1 << 1) | block.m_hi.p1);

            for (int i = 0; i < 16; i++)
            {
                uint w = s_bc7_weights4[i];
                uint iw = 64 - w;
                SetNoclampRgba(vals, i,
                    (r0 * iw + r1 * w + 32u) >> 6, 
                    (g0 * iw + g1 * w + 32u) >> 6, 
                    (b0 * iw + b1 * w + 32u) >> 6, 
                    (a0 * iw + a1 * w + 32u) >> 6);
            }

            LittleEndian.Pack (vals[block.m_hi.s00], m_block, 0);
            LittleEndian.Pack (vals[block.m_hi.s10], m_block, 4);
            LittleEndian.Pack (vals[block.m_hi.s20], m_block, 8);
            LittleEndian.Pack (vals[block.m_hi.s30], m_block, 12);

            LittleEndian.Pack (vals[block.m_hi.s01], m_block, 16);
            LittleEndian.Pack (vals[block.m_hi.s11], m_block, 20);
            LittleEndian.Pack (vals[block.m_hi.s21], m_block, 24);
            LittleEndian.Pack (vals[block.m_hi.s31], m_block, 28);

            LittleEndian.Pack (vals[block.m_hi.s02], m_block, 32);
            LittleEndian.Pack (vals[block.m_hi.s12], m_block, 36);
            LittleEndian.Pack (vals[block.m_hi.s22], m_block, 40);
            LittleEndian.Pack (vals[block.m_hi.s32], m_block, 44);

            LittleEndian.Pack (vals[block.m_hi.s03], m_block, 48);
            LittleEndian.Pack (vals[block.m_hi.s13], m_block, 52);
            LittleEndian.Pack (vals[block.m_hi.s23], m_block, 56);
            LittleEndian.Pack (vals[block.m_hi.s33], m_block, 60);

            return true;
        }

        static void SetNoclampRgba (uint[] vals, int dst, uint sr, uint sg, uint sb, uint sa)
        {
            vals[dst] = (sr & 0xFF) | ((sg & 0xFF) << 8) | ((sb & 0xFF) << 16) | ((sa & 0xFF) << 24);
        }

        static uint bc7_dequant (uint val, uint pbit, int val_bits)
        {
            int total_bits = val_bits + 1;
            val = (val << 1) | pbit;
            val <<= (8 - total_bits);
            val |= (val >> total_bits);
            return val;
        }

        static uint bc7_dequant (uint val, int val_bits)
        {
            val <<= (8 - val_bits);
            val |= (val >> val_bits);
            return val;
        }

        static uint bc7_interp2 (uint l, uint h, uint w)
        {
            return (l * (64 - s_bc7_weights2[w]) + h * s_bc7_weights2[w] + 32) >> 6;
        }

        static uint bc7_interp3 (uint l, uint h, uint w)
        {
            return (l * (64 - s_bc7_weights3[w]) + h * s_bc7_weights3[w] + 32) >> 6;
        }

        static uint bc7_interp4 (uint l, uint h, uint w)
        {
            return (l * (64 - s_bc7_weights4[w]) + h * s_bc7_weights4[w] + 32) >> 6;
        }

        static uint bc7_interp (uint l, uint h, uint w, int bits)
        {
            switch (bits)
            {
            case 2: return bc7_interp2 (l, h, w);
            case 3: return bc7_interp3 (l, h, w);
            case 4: return bc7_interp4 (l, h, w);
            default: return 0;
            }
        }

        static readonly uint[] s_bc7_weights2 = { 0, 21, 43, 64 };
        static readonly uint[] s_bc7_weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
        static readonly uint[] s_bc7_weights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

        static readonly byte[] s_bc7_partition2 = {
            0,0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,		0,0,0,1,0,0,0,1,0,0,0,1,0,0,0,1,
            0,1,1,1,0,1,1,1,0,1,1,1,0,1,1,1,		0,0,0,1,0,0,1,1,0,0,1,1,0,1,1,1,
            0,0,0,0,0,0,0,1,0,0,0,1,0,0,1,1,		0,0,1,1,0,1,1,1,0,1,1,1,1,1,1,1,
            0,0,0,1,0,0,1,1,0,1,1,1,1,1,1,1,		0,0,0,0,0,0,0,1,0,0,1,1,0,1,1,1,
            0,0,0,0,0,0,0,0,0,0,0,1,0,0,1,1,		0,0,1,1,0,1,1,1,1,1,1,1,1,1,1,1,
            0,0,0,0,0,0,0,1,0,1,1,1,1,1,1,1,		0,0,0,0,0,0,0,0,0,0,0,1,0,1,1,1,
            0,0,0,1,0,1,1,1,1,1,1,1,1,1,1,1,		0,0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,
            0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1,		0,0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,
            0,0,0,0,1,0,0,0,1,1,1,0,1,1,1,1,		0,1,1,1,0,0,0,1,0,0,0,0,0,0,0,0,
            0,0,0,0,0,0,0,0,1,0,0,0,1,1,1,0,		0,1,1,1,0,0,1,1,0,0,0,1,0,0,0,0,
            0,0,1,1,0,0,0,1,0,0,0,0,0,0,0,0,		0,0,0,0,1,0,0,0,1,1,0,0,1,1,1,0,
            0,0,0,0,0,0,0,0,1,0,0,0,1,1,0,0,		0,1,1,1,0,0,1,1,0,0,1,1,0,0,0,1,
            0,0,1,1,0,0,0,1,0,0,0,1,0,0,0,0,		0,0,0,0,1,0,0,0,1,0,0,0,1,1,0,0,
            0,1,1,0,0,1,1,0,0,1,1,0,0,1,1,0,		0,0,1,1,0,1,1,0,0,1,1,0,1,1,0,0,
            0,0,0,1,0,1,1,1,1,1,1,0,1,0,0,0,		0,0,0,0,1,1,1,1,1,1,1,1,0,0,0,0,
            0,1,1,1,0,0,0,1,1,0,0,0,1,1,1,0,		0,0,1,1,1,0,0,1,1,0,0,1,1,1,0,0,
            0,1,0,1,0,1,0,1,0,1,0,1,0,1,0,1,		0,0,0,0,1,1,1,1,0,0,0,0,1,1,1,1,
            0,1,0,1,1,0,1,0,0,1,0,1,1,0,1,0,		0,0,1,1,0,0,1,1,1,1,0,0,1,1,0,0,
            0,0,1,1,1,1,0,0,0,0,1,1,1,1,0,0,		0,1,0,1,0,1,0,1,1,0,1,0,1,0,1,0,
            0,1,1,0,1,0,0,1,0,1,1,0,1,0,0,1,		0,1,0,1,1,0,1,0,1,0,1,0,0,1,0,1,
            0,1,1,1,0,0,1,1,1,1,0,0,1,1,1,0,		0,0,0,1,0,0,1,1,1,1,0,0,1,0,0,0,
            0,0,1,1,0,0,1,0,0,1,0,0,1,1,0,0,		0,0,1,1,1,0,1,1,1,1,0,1,1,1,0,0,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,		0,0,1,1,1,1,0,0,1,1,0,0,0,0,1,1,
            0,1,1,0,0,1,1,0,1,0,0,1,1,0,0,1,		0,0,0,0,0,1,1,0,0,1,1,0,0,0,0,0,
            0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,0,		0,0,1,0,0,1,1,1,0,0,1,0,0,0,0,0,
            0,0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,		0,0,0,0,0,1,0,0,1,1,1,0,0,1,0,0,
            0,1,1,0,1,1,0,0,1,0,0,1,0,0,1,1,		0,0,1,1,0,1,1,0,1,1,0,0,1,0,0,1,
            0,1,1,0,0,0,1,1,1,0,0,1,1,1,0,0,		0,0,1,1,1,0,0,1,1,1,0,0,0,1,1,0,
            0,1,1,0,1,1,0,0,1,1,0,0,1,0,0,1,		0,1,1,0,0,0,1,1,0,0,1,1,1,0,0,1,
            0,1,1,1,1,1,1,0,1,0,0,0,0,0,0,1,		0,0,0,1,1,0,0,0,1,1,1,0,0,1,1,1,
            0,0,0,0,1,1,1,1,0,0,1,1,0,0,1,1,		0,0,1,1,0,0,1,1,1,1,1,1,0,0,0,0,
            0,0,1,0,0,0,1,0,1,1,1,0,1,1,1,0,		0,1,0,0,0,1,0,0,0,1,1,1,0,1,1,1
        };

        static readonly byte[] s_bc7_partition3 = {
            0,0,1,1,0,0,1,1,0,2,2,1,2,2,2,2,		0,0,0,1,0,0,1,1,2,2,1,1,2,2,2,1,
            0,0,0,0,2,0,0,1,2,2,1,1,2,2,1,1,		0,2,2,2,0,0,2,2,0,0,1,1,0,1,1,1,
            0,0,0,0,0,0,0,0,1,1,2,2,1,1,2,2,		0,0,1,1,0,0,1,1,0,0,2,2,0,0,2,2,
            0,0,2,2,0,0,2,2,1,1,1,1,1,1,1,1,		0,0,1,1,0,0,1,1,2,2,1,1,2,2,1,1,
            0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,		0,0,0,0,1,1,1,1,1,1,1,1,2,2,2,2,
            0,0,0,0,1,1,1,1,2,2,2,2,2,2,2,2,		0,0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,
            0,1,1,2,0,1,1,2,0,1,1,2,0,1,1,2,		0,1,2,2,0,1,2,2,0,1,2,2,0,1,2,2,
            0,0,1,1,0,1,1,2,1,1,2,2,1,2,2,2,		0,0,1,1,2,0,0,1,2,2,0,0,2,2,2,0,
            0,0,0,1,0,0,1,1,0,1,1,2,1,1,2,2,		0,1,1,1,0,0,1,1,2,0,0,1,2,2,0,0,
            0,0,0,0,1,1,2,2,1,1,2,2,1,1,2,2,		0,0,2,2,0,0,2,2,0,0,2,2,1,1,1,1,
            0,1,1,1,0,1,1,1,0,2,2,2,0,2,2,2,		0,0,0,1,0,0,0,1,2,2,2,1,2,2,2,1,
            0,0,0,0,0,0,1,1,0,1,2,2,0,1,2,2,		0,0,0,0,1,1,0,0,2,2,1,0,2,2,1,0,
            0,1,2,2,0,1,2,2,0,0,1,1,0,0,0,0,		0,0,1,2,0,0,1,2,1,1,2,2,2,2,2,2,
            0,1,1,0,1,2,2,1,1,2,2,1,0,1,1,0,		0,0,0,0,0,1,1,0,1,2,2,1,1,2,2,1,
            0,0,2,2,1,1,0,2,1,1,0,2,0,0,2,2,		0,1,1,0,0,1,1,0,2,0,0,2,2,2,2,2,
            0,0,1,1,0,1,2,2,0,1,2,2,0,0,1,1,		0,0,0,0,2,0,0,0,2,2,1,1,2,2,2,1,
            0,0,0,0,0,0,0,2,1,1,2,2,1,2,2,2,		0,2,2,2,0,0,2,2,0,0,1,2,0,0,1,1,
            0,0,1,1,0,0,1,2,0,0,2,2,0,2,2,2,		0,1,2,0,0,1,2,0,0,1,2,0,0,1,2,0,
            0,0,0,0,1,1,1,1,2,2,2,2,0,0,0,0,		0,1,2,0,1,2,0,1,2,0,1,2,0,1,2,0,
            0,1,2,0,2,0,1,2,1,2,0,1,0,1,2,0,		0,0,1,1,2,2,0,0,1,1,2,2,0,0,1,1,
            0,0,1,1,1,1,2,2,2,2,0,0,0,0,1,1,		0,1,0,1,0,1,0,1,2,2,2,2,2,2,2,2,
            0,0,0,0,0,0,0,0,2,1,2,1,2,1,2,1,		0,0,2,2,1,1,2,2,0,0,2,2,1,1,2,2,
            0,0,2,2,0,0,1,1,0,0,2,2,0,0,1,1,		0,2,2,0,1,2,2,1,0,2,2,0,1,2,2,1,
            0,1,0,1,2,2,2,2,2,2,2,2,0,1,0,1,		0,0,0,0,2,1,2,1,2,1,2,1,2,1,2,1,
            0,1,0,1,0,1,0,1,0,1,0,1,2,2,2,2,		0,2,2,2,0,1,1,1,0,2,2,2,0,1,1,1,
            0,0,0,2,1,1,1,2,0,0,0,2,1,1,1,2,		0,0,0,0,2,1,1,2,2,1,1,2,2,1,1,2,
            0,2,2,2,0,1,1,1,0,1,1,1,0,2,2,2,		0,0,0,2,1,1,1,2,1,1,1,2,0,0,0,2,
            0,1,1,0,0,1,1,0,0,1,1,0,2,2,2,2,		0,0,0,0,0,0,0,0,2,1,1,2,2,1,1,2,
            0,1,1,0,0,1,1,0,2,2,2,2,2,2,2,2,		0,0,2,2,0,0,1,1,0,0,1,1,0,0,2,2,
            0,0,2,2,1,1,2,2,1,1,2,2,0,0,2,2,		0,0,0,0,0,0,0,0,0,0,0,0,2,1,1,2,
            0,0,0,2,0,0,0,1,0,0,0,2,0,0,0,1,		0,2,2,2,1,2,2,2,0,2,2,2,1,2,2,2,
            0,1,0,1,2,2,2,2,2,2,2,2,2,2,2,2,		0,1,1,1,2,0,1,1,2,2,0,1,2,2,2,0,
        };

        static readonly byte[] s_bc7_table_anchor_index_second_subset = {
            15,15,15,15,15,15,15,15,	15,15,15,15,15,15,15,15,
            15, 2, 8, 2, 2, 8, 8,15,	 2, 8, 2, 2, 8, 8, 2, 2,
            15,15, 6, 8, 2, 8,15,15,	 2, 8, 2, 2, 2,15,15, 6,
             6, 2, 6, 8,15,15, 2, 2,  	15,15,15,15,15, 2, 2,15
        };

        static readonly byte[] s_bc7_table_anchor_index_third_subset_1 = {
            3, 3,15,15, 8, 3,15,15,		 8, 8, 6, 6, 6, 5, 3, 3,
            3, 3, 8,15, 3, 3, 6,10,		 5, 8, 8, 6, 8, 5,15,15,
            8,15, 3, 5, 6,10, 8,15,		15, 3,15, 5,15,15,15,15,
            3,15, 5, 5, 5, 8, 5,10,		 5,10, 8,13,15,12, 3, 3
        };

        static readonly byte[] s_bc7_table_anchor_index_third_subset_2 = {
            15, 8, 8, 3,15,15, 3, 8,	15,15,15,15,15,15,15, 8,
            15, 8,15, 3,15, 8,15, 8,	 3,15, 6,10,15,15,10, 8,
            15, 3,15,10,10, 8, 9,10,	 6,15, 8,15, 3, 6, 6, 8,
            15, 3,15,15,15,15,15,15,	15,15,15,15, 3,15,15, 8
        };
    }
}
