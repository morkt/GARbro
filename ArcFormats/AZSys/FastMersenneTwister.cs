/**
 * @brief SIMD oriented Fast Mersenne Twister(SFMT)
 *
 * @author Mutsuo Saito (Hiroshima University)
 * @author Makoto Matsumoto (Hiroshima University)
 *
 * C# port by morkt
 *
 * Copyright (C) 2006, 2007 Mutsuo Saito, Makoto Matsumoto and Hiroshima University.
 * Copyright (C) 2012 Mutsuo Saito, Makoto Matsumoto, Hiroshima University and The University of Tokyo.
 * Copyright (C) 2013 Mutsuo Saito, Makoto Matsumoto and Hiroshima University.
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *
 *   * Redistributions of source code must retain the above copyright
 *     notice, this list of conditions and the following disclaimer.
 *   * Redistributions in binary form must reproduce the above
 *     copyright notice, this list of conditions and the following
 *     disclaimer in the documentation and/or other materials provided
 *     with the distribution.
 *   * Neither the names of Hiroshima University, The University of
 *     Tokyo nor the names of its contributors may be used to endorse
 *     or promote products derived from this software without specific
 *     prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */

namespace GameRes.Utility
{
    public class FastMersenneTwister
    {
        const int MEXP  = 19937;
        const int N     = MEXP / 128 + 1;
        const int N32   = N * 4;
        const int POS1	= 122;
        const int SL1	= 18;
        const int SL2	= 1;
        const int SR1	= 11;
        const int SR2	= 1;
        const uint MSK1	= 0xdfffffefU;
        const uint MSK2	= 0xddfecb7fU;
        const uint MSK3	= 0xbffaffffU;
        const uint MSK4	= 0xbffffff6U;
        const uint PARITY1	= 0x00000001U;
        const uint PARITY2	= 0x00000000U;
        const uint PARITY3	= 0x00000000U;
        const uint PARITY4	= 0x13c9e684U;

        uint[,]     m_state = new uint[N,4];
        int         m_idx;

        public FastMersenneTwister (uint seed)
        {
            SRand (seed);
        }

        public void SRand (uint seed)
        {
            uint prev = m_state[0,0] = seed;
            for (int i = 1; i < N32; i++)
            {
                int p = i >> 2;
                int k = i & 3;
                prev = (uint)(1812433253UL * (prev ^ (prev >> 30)) + (uint)i);
                m_state[p,k] = prev;
            }
            m_idx = N32;
            period_certification();
        }

        public uint GetRand32 ()
        {
            if (m_idx >= N32)
            {
                sfmt_gen_rand_all();
                m_idx = 0;
            }
            uint r = m_state[m_idx >> 2, m_idx & 3];
            m_idx++;
            return r;
        }

        /// <summary>
        /// This function fills the internal state array with pseudorandom integers.
        /// </summary>
        void sfmt_gen_rand_all ()
        {
            int i;
            int r1 = N - 2;
            int r2 = N - 1;
            for (i = 0; i < N - POS1; i++)
            {
                do_recursion (i, i, i + POS1, r1, r2);
                r1 = r2;
                r2 = i;
            }
            for (; i < N; i++)
            {
                do_recursion (i, i, i + POS1 - N, r1, r2);
                r1 = r2;
                r2 = i;
            }
        }

        /// <summary>
        /// This function represents the recursion formula.
        /// </summary>
        void do_recursion (int r, int a, int b, int c, int d)
        {
            var x = new uint[4];
            var y = new uint[4];
            lshift128 (x, a, SL2);
            rshift128 (y, c, SR2);
            m_state[r,0] = m_state[a,0] ^ x[0] ^ ((m_state[b,0] >> SR1) & MSK1)
                                        ^ y[0] ^ (m_state[d,0] << SL1);
            m_state[r,1] = m_state[a,1] ^ x[1] ^ ((m_state[b,1] >> SR1) & MSK2)
                                        ^ y[1] ^ (m_state[d,1] << SL1);
            m_state[r,2] = m_state[a,2] ^ x[2] ^ ((m_state[b,2] >> SR1) & MSK3)
                                        ^ y[2] ^ (m_state[d,2] << SL1);
            m_state[r,3] = m_state[a,3] ^ x[3] ^ ((m_state[b,3] >> SR1) & MSK4)
                                        ^ y[3] ^ (m_state[d,3] << SL1);
        }

        static readonly uint[] s_parity = { PARITY1, PARITY2, PARITY3, PARITY4 }; 

        /// <summary>
        /// This function certificate the period of 2^{MEXP}
        /// </summary>
        void period_certification ()
        {
            uint inner = 0;
            int i;

            for (i = 0; i < 4; i++)
                inner ^= m_state[0,i] & s_parity[i];
            for (i = 16; i > 0; i >>= 1)
                inner ^= inner >> i;
            inner &= 1;
            /* check OK */
            if (inner == 1)
                return;

            /* check NG, and modification */
            for (i = 0; i < 4; i++)
            {
                uint work = 1;
                for (int j = 0; j < 32; j++)
                {
                    if ((work & s_parity[i]) != 0)
                    {
                        m_state[0,i] ^= work;
                        return;
                    }
                    work = work << 1;
                }
            }
        }

        /// <summary>
        /// This function simulates SIMD 128-bit left shift.
        /// The 128-bit integer referenced by idx is shifted by (shift * 8) bits.
        /// This function simulates the LITTLE ENDIAN SIMD.
        /// </summary>
        /// <param name="result">the output of this function</param>
        /// <param name="idx">index within state array of the 128-bit data to be shifted</param>
        /// <param name="shift">the shift value</param>
        void lshift128 (uint[] result, int idx, int shift)
        {
            ulong th = ((ulong)m_state[idx,3] << 32) | ((ulong)m_state[idx,2]);
            ulong tl = ((ulong)m_state[idx,1] << 32) | ((ulong)m_state[idx,0]);

            ulong oh = th << (shift * 8);
            ulong ol = tl << (shift * 8);
            oh |= tl >> (64 - shift * 8);
            result[1] = (uint)(ol >> 32);
            result[0] = (uint)ol;
            result[3] = (uint)(oh >> 32);
            result[2] = (uint)oh;
        }

        void rshift128 (uint[] result, int idx, int shift)
        {
            ulong th = ((ulong)m_state[idx,3] << 32) | ((ulong)m_state[idx,2]);
            ulong tl = ((ulong)m_state[idx,1] << 32) | ((ulong)m_state[idx,0]);

            ulong oh = th >> (shift * 8);
            ulong ol = tl >> (shift * 8);
            ol |= th << (64 - shift * 8);
            result[1] = (uint)(ol >> 32);
            result[0] = (uint)ol;
            result[3] = (uint)(oh >> 32);
            result[2] = (uint)oh;
        }
    }
}
