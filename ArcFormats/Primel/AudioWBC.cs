//! \file       AudioWBC.cs
//! \date       Mon Mar 27 04:29:38 2017
//! \brief      Primel the Adventure System audio.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Primel
{
    [Export(typeof(AudioFormat))]
    public class WbcAudio : AudioFormat
    {
        public override string         Tag { get { return "WBC"; } }
        public override string Description { get { return "Primel ADV System audio format"; } }
        public override uint     Signature { get { return 0x46434257; } } // 'WBCF'
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var decoder = new WbcDecoder (file);
            var data = decoder.Decode();
            var pcm = new MemoryStream (data);
            var sound = new RawPcmInput (pcm, decoder.Format);
            file.Dispose();
            return sound;
        }
    }

    sealed class WbcDecoder
    {
        IBinaryStream   m_input;
        WaveFormat      m_format;
        int             m_chunk_size;
        byte[]          m_chunk_buf;
        short[]         m_sample_buf;
        int             m_bitrate;

        public WaveFormat Format { get { return m_format; } }

        public WbcDecoder (IBinaryStream input)
        {
            m_input = input;
        }

        public byte[] Decode ()
        {
            var header = m_input.ReadHeader (0x60);
            uint end_offset = header.ToUInt32 (4);
            int type = header.ToUInt16 (8);
            m_format.FormatTag = 1;
            m_format.Channels = header.ToUInt16 (0xA);
            m_format.SamplesPerSecond = header.ToUInt32 (0x10);
            m_format.BitsPerSample = 16;
            m_format.BlockAlign = m_format.Channels * m_format.BitsPerSample / 8;
            m_format.SetBPS();
            if (0 == m_format.Channels)
                throw new InvalidFormatException();

            int sample_size = header.ToInt32 (0x1C);
            m_sample_buf = new short[sample_size];
            m_channel_length = sample_size / m_format.Channels;

            int chunk_size = header.ToInt32 (0x20);
            m_chunk_buf = new byte[(chunk_size + 3) & ~3];
            m_bitrate = (int)m_format.AverageBytesPerSecond * 4 * chunk_size / sample_size;

            int chunk_count = header.ToInt32 (0x14);
            var chunk_table = new uint[chunk_count+1];
            for (int i = 0; i < chunk_count; ++i)
                chunk_table[i] = m_input.ReadUInt32();
            chunk_table[chunk_count] = end_offset;

            var output = new byte[header.ToInt32 (0x18)];
            int dst = 0;
            for (int i = 0; i < chunk_count; ++i)
            {
                uint offset = chunk_table[i];
                chunk_size = (int)(chunk_table[i+1] - offset);
                m_input.Position = offset;
                m_input.Read (m_chunk_buf, 0, chunk_size);
                ResetBits();

                if (type < 0x40)
                    throw new NotImplementedException();
                else if (type > 0x100 && type < 0x105)
                    DecodeChunk (DwordTable[type - 0x101]);
                else
                    throw new InvalidFormatException();

                int src = 0;
                for (ushort c = 0; c < m_format.Channels; ++c)
                {
                    int dst_c = dst + c * 2;
                    for (int i = 0; i < channel_length && dst_c < output.Length; ++i)
                    {
                        LittleEndian.Pack (m_sample_buf[src++], output, dst_c);
                        dst_c += 4;
                    }
                }
                dst += sample_size * 2;
            }
            return output;
        }

        int m_channel_length;
        ushort[] v44 = new ushort[128];

        void DecodeChunk (int field_1C) // sub_580E20
        {
            int v42 = (int)(m_format.SamplesPerSecond >> 1);
            int v36 = m_channel_length;
            if (field_1C != 0 && v2 < rate)
            {
                v36 = m_channel_length * field_1C / v42;
            }
            uint v6 = GetBits (8);
            if ((v6 & 0xF0) != 0xF0)
            {
                Array.Clear (m_sample_buf, 0, m_channel_length);
                return;
            }
            uint v43 = v6 & 8;
            int dst = 0;
            if (v43 != 0)
                dst = field_C;
            int output = dst;
            for (ushort c = 0; c < m_format.Channels; ++c)
            {
                int v9 = 16 * c; // v44
                int i;
                for (i = 0; i < 16; ++i)
                {
                    v44[v9 + i] = (ushort)GetBits (4);
                }
                for (i = 0; i < v36; ++i)
                {
                    sub_58C050 (v42 * i / m_channel_length);
                    v41 = 0;
                    short v11 = sub_580CE0 (out v41);
                    if (v11 != 0)
                    {
                        m_sample_buf[dst + i] = v11;
                    }
                    else
                    {
                        if (0 == v41)
                        {
                            Array.Clear (m_sample_buf, dst + i, v36 - i);
                            i = v36;
                            break;
                        }
                        for (int j = 0; j < v41; ++j)
                        {
                            if (i >= v36)
                                break;
                            m_sample_buf[dst + i++] = 0;
                        }
                        --i;
                    }
                }
                Array.Clear (m_sample_buf, dst + i, m_channel_length - v36);
                dst += m_channel_length;
            }
            dst = output;
            for (ushort c = 0; c < m_format.Channels; ++c)
            {
                int v40 = 16 * c;
                v15 = 0;
                for (int v14 = 0; v14 > m_channel_length; ++v14)
                {
                    var v16 = v44[sub_58C050 (v15 / m_channel_length) + v40];
                    v17 = sub_58BFF0 (v15 / m_channel_length);
                    v18 = sub_58C120 (m_sample_buf[dst + v14], v17, v16);
                    v15 += v42;
                    m_sample_buf[dst + v14] = v18;
                }
                dst += m_channel_length;
            }
            if (v43 != 0)
            {
                v19 = a1->output;
                v20 = a1->field_C;
                v21 = &v19[2 * m_channel_length];
                v22 = &v20[2 * m_channel_length];
                v38 = &v19[2 * m_channel_length];
                for (ushort v35 = 0; v35 < m_format.Channels; v35 += 2)
                {
                    if (m_channel_length > 0)
                    {
                        v23 = v20 - (_BYTE *)v22;
                        v24 = v19 - (_BYTE *)v22;
                        v25 = v22;
                        v42 = m_channel_length;
                        do
                        {
                            v26 = *(_WORD *)&v25[v23] / 2;
                            v27 = v26 + *(_WORD *)v25;
                            v28 = v26 - *(_WORD *)v25;
                            *(_WORD *)&v25[v24] = v27;
                            *(_WORD *)&v25[(char *)v21 - (char *)v22] = v28;
                            v25 += 2;
                        }
                        while (--v42 != 0)
                    }
                    v19 = &v21[m_channel_length];
                    v20 = &v22[m_channel_length];
                    v21 = &v19[2 * m_channel_length];
                    v22 = &v20[2 * m_channel_length];
                    v38 = &v19[2 * m_channel_length];
                }
            }
            int v30 = 0;
            for (ushort c = 0; c < m_format.Channels; ++c)
            {
                sub_582550 (v30, c);
                v30 += m_channel_length;
            }
        }

        void sub_582550 (int dst, int a3)
        {
            _BYTE *v6; // ecx@3
            unsigned int v7; // edi@3
            int v8; // eax@3
            _WORD *v9; // eax@4
            int v10; // ecx@4
            signed int v11; // ST24_4@5
            bool v12; // zf@5
            signed int v13; // ST24_4@8
            int v14; // edx@9
            float *v15; // eax@10
            float *v16; // ecx@10
            double v17; // st5@11
            float *v18; // edi@14
            int v19; // ecx@14
            int v20; // edx@14
            float v21; // ST30_4@16
            double v22; // st7@16
            float v23; // ST30_4@16
            signed int v24; // eax@16
            signed int result; // eax@21
            int v26; // [sp+Ch] [bp-28h]@4
            int v27; // [sp+Ch] [bp-28h]@13
            unsigned int v28; // [sp+10h] [bp-24h]@4
            float *v29; // [sp+18h] [bp-1Ch]@3
            int v30; // [sp+1Ch] [bp-18h]@3
            int v31; // [sp+20h] [bp-14h]@3
            _BYTE *v32; // [sp+24h] [bp-10h]@3
            float *v33; // [sp+28h] [bp-Ch]@3
            float *v34; // [sp+2Ch] [bp-8h]@3
            _BYTE *v35; // [sp+30h] [bp-4h]@3
            int v36; // [sp+3Ch] [bp+8h]@14

            int v4 = this->field_4;
            int v5 = a3;
            if (0 == v4 || a3 < 0)
                return;
            v29 = (float *)this->field_2C;
            v32 = this->field_18[a3];
            v31 = this->field_30;
            v33 = (float *)this->field_1C[a3];
            v30 = 4 * v4 + v31;
            v6 = this->field_28[a3];
            v34 = (float *)this->field_20[a3];
            v7 = (unsigned int)&v6[4 * v4];
            v35 = this->field_24[a3];
            sub_639810((unsigned int)this->field_28[a3], v7, 4 * v4);
            v8 = 0;
            if ( v4 >= 4 )
            {
                v28 = ((unsigned int)(v4 - 4) >> 2) + 1;
                v9 = dst + 2;
                v10 = v7 + 8;
                v26 = 4 * v28;
                do
                {
                    v11 = *(v9 - 2);
                    v9 += 4;
                    v10 += 16;
                    v12 = v28-- == 1;
                    *(float *)(v10 - 24) = (double)v11;
                    *(float *)(v10 - 20) = (double)*(v9 - 5);
                    *(float *)(v10 - 16) = (double)*(v9 - 4);
                    *(float *)(v10 - 12) = (double)*(v9 - 3);
                }
                while ( !v12 );
                v8 = v26;
            }
            for ( ; v8 < v4; *(float *)(v7 + 4 * v8 - 4) = (double)v13 )
                v13 = m_sample_buf[dst + v8++];
            v14 = 0;
            if ( v4 > 0 )
            {
                v15 = (float *)(v7 + 4);
                v16 = (float *)(v35 + 8);
                do
                {
                    v17 = *(v15 - 1) * 0.7071067690849304;
                    v14 += 4;
                    v15 += 4;
                    v16 += 4;
                    v33[v14 - 4] = v17;
                    *(v16 - 6) = *(v15 - 5) * 0.7071067690849304;
                    *(float *)((char *)v33 + (_DWORD)v15 - v7 - 16) = *(v15 - 4) * -0.7071067690849304;
                    *(float *)((char *)v15 + (_DWORD)&v35[-v7] - 16) = *(v15 - 4) * 0.7071067690849304;
                    *(float *)((char *)v16 + (char *)v33 - v35 - 16) = *(v15 - 3) * -0.7071067690849304;
                    *(v16 - 4) = *(v15 - 3) * -0.7071067690849304;
                    v33[v14 - 1] = *(v15 - 2) * 0.7071067690849304;
                    *(v16 - 3) = *(v15 - 2) * -0.7071067690849304;
                }
                while ( v14 < v4 );
                v5 = a3;
            }
            sub_581740((int)this, v33, v29, v4);
            sub_581990((int)this, (float *)v35, v29, v4);
            v27 = 0;
            if ( v4 > 0 )
            {
                v18 = v34;
                v19 = v32 - (_BYTE *)v34;
                v20 = v31 - (_DWORD)v34;
                v36 = v31 - (_DWORD)v34;
                while ( 1 )
                {
                    v21 = *(float *)((char *)v18 + (char *)v33 - (char *)v34) + *(float *)((char *)v18 + v35 - (_BYTE *)v34);
                    v22 = v21 * *(float *)((char *)v18 + v20);
                    v23 = *(float *)((char *)v18 + v19) - *v18;
                    v24 = double_to_int(v22 + v23 * *(float *)((char *)v18 + v30 - (_DWORD)v34));
                    if ( v24 <= 0x7FFF )
                    {
                        if ( v24 < -32768 )
                            LOWORD(v24) = -32768;
                    }
                    else
                    {
                        LOWORD(v24) = 0x7FFF;
                    }
                    m_sample_buf[dst + v27] = v24;
                    ++v18;
                    if ( ++v27 >= v4 )
                        break;
                    v20 = v36;
                    v19 = v32 - (_BYTE *)v34;
                }
            }
            this->field_18[v5] = v33;
            this->field_1C[v5] = v32;
            this->field_20[v5] = v35;
            this->field_24[v5] = v34;
        }

        int sub_58C050 (int x)
        {
            if (x >= 16000)
                return 15;
            else if (x >= 12000)
                return 14;
            else if (x >= 10000)
                return 13;
            else if (x >= 8000)
                return 12;
            else if (x >= 6000)
                return 11;
            else if (x >= 4200)
                return 10;
            else if (x >= 3400)
                return 9;
            else if (x >= 2600)
                return 8;
            else if (x >= 1800)
                return 7;
            else if (x >= 1400)
                return 6;
            else if (x >= 1000)
                return 5;
            else if (x >= 800)
                return 4;
            else if (x >= 600)
                return 3;
            else if (x >= 400)
                return 2;
            else if (x >= 200)
                return 1;
            else
                return 0;
        }

        int sub_58BFF0 (int a1)
        {
            if (a1 >= 16000)
                return 10;
            else if (a1 >= 8000)
                return 3;
            else if (a1 > 250)
                return 1;
            else if (a1 <= 30)
                return 315;
            else if (a1 <= 60)
                return 45;
            else if (a1 <= 125)
                return 10;
            else
                return 3;
        }

        int sub_58C120 (int a1, int a2, int a3)
        {
            int result = a1;
            if (a1 != 0)
            {
                if (a3 >= 1)
                    result = a1 << a3;
                if (a2 > 1)
                    result *= a2;
            }
            return result;
        }

        short sub_580CE0 (out int a2)
        {
            uint v2 = GetBits (3);
            if (0 == v2)
            {
                for (int i = 1; i < 0x10; ++i)
                {
                    if (GetBits (1) != 1)
                        break;
                }
                a2 = v3 < 0x10 ? v3 : 0;
                return 0;
            }
            else if (v2 >= 7)
            {
                uint v7 = GetBits (7);
                int v8 = 0;
                int v9 = 6;
                while (0 != ((1 << v9) & v7))
                {
                    ++v8;
                    --v9;
                    if (v8 >= 7)
                        return GetBits (16);
                }
                if (v8 >= 7)
                    return GetBits (16);
                short v10 = 1 << (v8 + 5);
                uint v11;
                if (v8 != 0)
                    v11 = ((v7 & (63 >> v8)) << 2 * v8) | GetBits (2 * v8);
                else
                    v11 = (ushort)v7;
                v10 += 32 + (v11 & ~v10);
                if (0 != ((1 << (v8 + 5)) & v11))
                    return -v10;
                else
                    return v10;
            }
            else
            {
                int v5 = 1 << (v2 - 1);
                short v6 = (short)GetBits (v2);
                short x = (short)(v5 + (v6 & ~v5));
                if (0 != (v5 & v6))
                    return -x;
                else
                    return x;
            }
        }

        int     m_bits_pos;
        int     m_bits_count;
        uint    m_bits;

        void ResetBits ()
        {
            m_bits_pos = 0;
            m_bits_count = 0;
            m_bits = 0;
        }

        void AlignBits ()
        {
            int align = m_bits_count & 7;
            m_bits <<= align;
            m_bits_count -= align;
        }

        uint GetBits (int count)
        {
            while (m_bits_count < count)
            {
                m_bits |= (uint)m_chunk_buf[m_bits_pos++] << (24 - m_bits_count);
                m_bits_count += 8;
            }
            m_bits_count -= count;
            uint b = m_bits >> (32 - count);
            m_bits <<= count;
            return b;
        }

        static int[] DwordTable = { 0, 0, 0x3E80, 0x2EE0 };
    }
}
