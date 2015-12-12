//! \file       CmvsMD5.cs
//! \date       Mon Nov 30 13:29:27 2015
//! \brief      Cmvs engine MD5 update algorithm.
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

namespace GameRes.Formats.Cmvs
{
    public enum Md5Variant { A, B }

    public abstract class MD5
    {
        protected uint[]  m_state;
        protected uint[]  m_buffer;

        static readonly byte[,] ShiftsTable = {
            { 7, 12, 17, 22 }, { 5, 9, 14, 20 }, { 4, 11, 16, 23 }, { 6, 10, 15, 21 },
        };

        static readonly uint[] SineTable = {
            0xd76aa478, 0xe8c7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304613, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b901122, 0xfd987193, 0xa679438e, 0x49b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xe9b6c7aa, 0xd62f105d, 0x02441453, 0xd8a1e681, 0xe7d3fbc8,
            0x21e1cde6, 0xc33707d6, 0xf4d50d87, 0x455a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        public MD5 ()
        {
            m_state = InitState();
            m_buffer = new uint[16];
        }

        static public MD5 Create (Md5Variant variant)
        {
            if (Md5Variant.A == variant)
                return new Md5VariantA();
            else
                return new Md5VariantB();
        }

        protected abstract uint[] InitState ();
        protected abstract void SetResult (uint[] data);

        public void Compute (uint[] data)
        {
            m_buffer[0] = data[0];
            m_buffer[1] = data[1];
            m_buffer[2] = data[2];
            m_buffer[3] = data[3];
            m_buffer[4] = 0x80;
            m_buffer[14] = 0x80;
            Transform();
            SetResult (data);
        }

        void Transform ()
        {
            uint a = m_state[0];
            uint b = m_state[1];
            uint c = m_state[2];
            uint d = m_state[3];

            for (int i = 0; i < 64; ++i)
            {
                uint f;
                int g;
                if (i < 16)
                {
                    f = d ^ (b & (c ^ d));
                    g = i;
                }
                else if (i < 32)
                {
                    f = c ^ (d & (b ^ c));
                    g = (5 * i + 1) & 0xF;
                }
                else if (i < 48)
                {
                    f = b ^ c ^ d;
                    g = (3 * i + 5) & 0xF;
                }
                else
                {
                    f = c ^ (b | ~d);
                    g = (7 * i) & 0xF;
                }
                uint t = d;
                d = c;
                c = b;
                b += Binary.RotL (a + f + m_buffer[g] + SineTable[i], ShiftsTable[i>>4, i&3]);
                a = t;
            }

            m_state[0] += a;
            m_state[1] += b;
            m_state[2] += c;
            m_state[3] += d;
        }
    }

    public class Md5VariantA : MD5
    {
        protected override uint[] InitState ()
        {
            return new uint[] { 0xC74A2B01, 0xE7C8AB8F, 0xD8BEDC4E, 0x7302A4C5 };
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[3];
            data[1] = m_state[1];
            data[2] = m_state[2];
            data[3] = m_state[0];
        }
    }

    public class Md5VariantB : MD5
    {
        protected override uint[] InitState ()
        {
            return new uint[] { 0x53FE9B2C, 0xF2C93EA8, 0xEE81BA59, 0xA2C8973E };
        }

        protected override void SetResult (uint[] data)
        {
            data[0] = m_state[1] ^ 0x49875325;
            data[1] = m_state[2] + 0x54F46D7D;
            data[2] = m_state[3] ^ 0xAD7948B7;
            data[3] = m_state[0] + 0x1D0638AD;
        }
    }
}
