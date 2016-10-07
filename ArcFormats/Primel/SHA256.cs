//! \file       SHA256.cs
//! \date       Sat Oct 01 10:25:28 2016
//! \brief      Slightly modified SHA256 hash algorithm implementation.
//

using System;
using GameRes.Utility;

namespace GameRes.Formats.Primel
{
    /// <summary>
    /// limited implementation of SHA256 hash function.
    /// differs from the standard SHA256 by rotation operands in TransformBlock method.
    /// as these hashes in Primel are used to generate keys only, this implementation works
    /// for messages shorter than 56 bytes only.
    /// </summary>
    public class SHA256
    {
        uint[]  m_state;
        uint[]  m_data;

        const int BlockSize = 64;

        public SHA256 ()
        {
	        m_state = new uint[] {
                0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
                0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19
            };
            m_data = new uint[BlockSize / sizeof(uint)];
        }

        public byte[] ComputeHash (byte[] data)
        {
            if (data.Length > 55)
                throw new ApplicationException ("[SHA256] message is too long");
            CopyBigEndian (data, 0, data.Length);
            m_data[m_data.Length-1] = (uint)(data.Length * 8);
            TransformBlock();

            var hash = new byte[32];
            int dst = 0;
            for (int i = 0; i < 8; ++i)
            {
                hash[dst++] = (byte)(m_state[i] >> 24);
                hash[dst++] = (byte)(m_state[i] >> 16);
                hash[dst++] = (byte)(m_state[i] >> 8);
                hash[dst++] = (byte)m_state[i];
            }
            return hash;
        }

        void CopyBigEndian (byte[] data, int src, int size)
        {
            int word_count = size / 4;
            int i;
            for (i = 0; i < word_count; ++i)
            {
                m_data[i] = BigEndian.ToUInt32 (data, src);
                src += 4;
            }
            if (size < BlockSize)
            {
                m_data[i] = 0;
                int shift = 24;
                for (int j = size & 3; j > 0; --j)
                {
                    m_data[i] |= (uint)data[src++] << shift;
                    shift -= 8;
                }
                m_data[i] |= 0x80u << shift;
                while (++i < m_data.Length)
                    m_data[i] = 0;
            }
        }

        void TransformBlock ()
        {
            uint a = m_state[0];
            uint b = m_state[1];
            uint c = m_state[2];
            uint d = m_state[3];
            uint e = m_state[4];
            uint f = m_state[5];
            uint g = m_state[6];
            uint h = m_state[7];
            for (int j = 0; j < 64; j += 16)
            for (int i = 0; i < 16; ++i)
            {
                if (j > 0)
                {
                    uint x = m_data[(i - 15) & 15];
                    uint y = m_data[(i - 2) & 15];
                    x = Binary.RotL (x, 7)  ^ Binary.RotL (x, 18) ^ (x >> 3);
                    y = Binary.RotL (y, 17) ^ Binary.RotL (y, 19) ^ (y >> 10);
                    m_data[i] += x + y + m_data[(i-7)&15];
                }
                uint s0 = Binary.RotL (a, 2) ^ Binary.RotL (a, 13) ^ Binary.RotR (a, 10);
                uint maj = (a & b) ^ (b & c) ^ (c & a);
                uint t0 = s0 + maj;
                uint s1 = Binary.RotL (e, 6) ^ Binary.RotL (e, 11) ^ Binary.RotR (e, 7);
                uint ch = (e & f) ^ (~e & g);
                uint t1 = h + s1 + ch + SHA256_K[i+j] + m_data[i];
                h = g;
                g = f;
                f = e;
                e = d + t1;
                d = c;
                c = b;
                b = a;
                a = t0 + t1;
            }
            m_state[0] += a;
            m_state[1] += b;
            m_state[2] += c;
            m_state[3] += d;
            m_state[4] += e;
            m_state[5] += f;
            m_state[6] += g;
            m_state[7] += h;
        }

        static readonly uint[] SHA256_K = {
            0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5,
            0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
            0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3,
            0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
            0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC,
            0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
            0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7,
            0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
            0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13,
            0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
            0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3,
            0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
            0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5,
            0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
            0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208,
            0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
        };
    }
}
