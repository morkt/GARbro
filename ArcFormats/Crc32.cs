//! \file       Crc32.cs
//! \date       Fri Aug 19 09:18:42 2016
//! \brief      Crc32 with normal polynomial representation.
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
    /// <summary>
    /// Crc32 with normal polynomial representation.
    /// </summary>
    public sealed class Crc32Normal : ICheckSum
    {
        private static readonly uint[] crc_table = InitializeTable();

        public static uint[] Table { get { return crc_table; } }

        private static uint[] InitializeTable ()
        {
            const uint polynomial = 0x04C11DB7;
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n << 24;
                for (int k = 0; k < 8; k++)
                {
                    if (0 != (c & 0x80000000u))
                        c = polynomial ^ (c << 1);
                    else
                        c <<= 1;
                }
                table[n] = c;
            }
            return table;
        }
   
        public static uint UpdateCrc (uint init_crc, byte[] data, int pos, int length)
        {
            uint c = init_crc;
            for (int n = 0; n < length; n++)
                c = crc_table[(c >> 24) ^ data[pos+n]] ^ (c << 8);
            return c;
        }
   
        public static uint Compute (byte[] buf, int pos, int len)
        {
            return ~UpdateCrc (0xffffffff, buf, pos, len);
        }

        private uint m_crc = 0xffffffff;
        public  uint Value { get { return ~m_crc; } }

        public void Update (byte[] buf, int pos, int len)
        {
            m_crc = UpdateCrc (m_crc, buf, pos, len);
        }
    }
}
