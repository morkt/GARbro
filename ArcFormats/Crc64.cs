//! \file       Crc64.cs
//! \date       Sun Apr 24 22:07:30 2016
//! \brief      Crc-64 implementation.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Utility
{
    public sealed class Crc64
    {
        const ulong Polynomial = 0x42F0E1EBA9EA3693ul;

        private static readonly ulong[] crc_table = InitializeTable (Polynomial);

        public static ulong[] Table { get { return crc_table; } }

        private static ulong[] InitializeTable (ulong poly)
        {
            var table = new ulong[256];
            for (uint i = 0; i < 256; ++i)
            {
                ulong crc = (ulong)i << 56;
                for (int j = 0; j < 8; ++j)
                {
                    if ((crc >> 63) != 0)
                        crc = (crc << 1) ^ poly;
                    else
                        crc <<= 1;
                }
                table[i] = crc;
            }
            return table;
        }
   
        public static ulong UpdateCrc (ulong crc, byte[] buf, int pos, int len)
        {
            for (int n = 0; n < len; n++)
                crc = crc_table[((crc >> 56) ^ buf[pos+n]) & 0xFF] ^ (crc << 8);
            return crc;
        }
   
        public static ulong Compute (byte[] buf, int pos, int len)
        {
            return ~UpdateCrc (~0ul, buf, pos, len);
        }

        private ulong m_crc = ~0ul;
        public  ulong Value { get { return ~m_crc; } }

        public void Update (byte[] buf, int pos, int len)
        {
            m_crc = UpdateCrc (m_crc, buf, pos, len);
        }
    }
}
