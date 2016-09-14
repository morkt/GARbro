//! \file       Checksum.cs
//! \date       Sun Apr 24 18:09:11 2016
//! \brief      Various checksum algorithms implementations.
//
// Copyright (C) 2014-2016 by morkt
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

using System.IO;

namespace GameRes.Utility
{
    public interface ICheckSum
    {
        uint Value { get; }
        void Update (byte[] buf, int pos, int len);
    }

    public sealed class Crc32 : ICheckSum
    {
        /* Table of CRCs of all 8-bit messages. */
        private static readonly uint[] crc_table = InitializeTable();

        public static uint[] Table { get { return crc_table; } }

        /* Make the table for a fast CRC. */
        private static uint[] InitializeTable ()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    if (0 != (c & 1))
                        c = 0xedb88320 ^ (c >> 1);
                    else
                        c = c >> 1;
                }
                table[n] = c;
            }
            return table;
        }
   
        /* Update a running CRC with the bytes buf[0..len-1]--the CRC
           should be initialized to all 1's, and the transmitted value
           is the 1's complement of the final running CRC (see the
           crc() routine below)). */
        public static uint UpdateCrc (uint crc, byte[] buf, int pos, int len)
        {
            uint c = crc;
            for (int n = 0; n < len; n++)
                c = crc_table[(c ^ buf[pos+n]) & 0xff] ^ (c >> 8);
            return c;
        }
   
        /* Return the CRC of the bytes buf[0..len-1]. */
        public static uint Compute (byte[] buf, int pos, int len)
        {
            return UpdateCrc (0xffffffff, buf, pos, len) ^ 0xffffffff;
        }

        private uint m_crc = 0xffffffff;
        public  uint Value { get { return m_crc^0xffffffff; } }

        public void Update (byte[] buf, int pos, int len)
        {
            m_crc = UpdateCrc (m_crc, buf, pos, len);
        }
    }

    /* Adler32 implementation -- borrowed from the 'zlib' compression library.

    Copyright (C) 1995-2013 Jean-loup Gailly and Mark Adler

    This software is provided 'as-is', without any express or implied
    warranty.  In no event will the authors be held liable for any damages
    arising from the use of this software.

    Permission is granted to anyone to use this software for any purpose,
    including commercial applications, and to alter it and redistribute it
    freely, subject to the following restrictions:

    1. The origin of this software must not be misrepresented; you must not
        claim that you wrote the original software. If you use this software
        in a product, an acknowledgment in the product documentation would be
        appreciated but is not required.
    2. Altered source versions must be plainly marked as such, and must not be
        misrepresented as being the original software.
    3. This notice may not be removed or altered from any source distribution.
    */

    public sealed class Adler32 : ICheckSum
    {
        const uint BASE = 65521;      /* largest prime smaller than 65536 */
        const int  NMAX = 5552;

        public static uint Compute (byte[] buf, int pos, int len)
        {
            if (0 == len)
                return 1;
            unsafe
            {
                fixed (byte* ptr = &buf[pos])
                {
                    return Update (1, ptr, len);
                }
            }
        }

        public unsafe static uint Compute (byte* buf, int len)
        {
            return Update (1, buf, len);
        }

        private unsafe static uint Update (uint adler, byte* buf, int len)
        {
            /* split Adler-32 into component sums */
            uint sum2 = (adler >> 16) & 0xffff;
            adler &= 0xffff;

            /* in case user likes doing a byte at a time, keep it fast */
            if (1 == len) {
                adler += *buf;
                if (adler >= BASE)
                    adler -= BASE;
                sum2 += adler;
                if (sum2 >= BASE)
                    sum2 -= BASE;
                return adler | (sum2 << 16);
            }

            /* in case short lengths are provided, keep it somewhat fast */
            if (len < 16) {
                while (0 != len--) {
                    adler += *buf++;
                    sum2 += adler;
                }
                if (adler >= BASE)
                    adler -= BASE;
                sum2 %= BASE;            /* only added so many BASE's */
                return adler | (sum2 << 16);
            }

            /* do length NMAX blocks -- requires just one modulo operation */
            while (len >= NMAX) {
                len -= NMAX;
                int n = NMAX / 16;          /* NMAX is divisible by 16 */
                do {
                    /* 16 sums unrolled */
                    adler += buf[0];  sum2 += adler;
                    adler += buf[1];  sum2 += adler;
                    adler += buf[2];  sum2 += adler;
                    adler += buf[3];  sum2 += adler;
                    adler += buf[4];  sum2 += adler;
                    adler += buf[5];  sum2 += adler;
                    adler += buf[6];  sum2 += adler;
                    adler += buf[7];  sum2 += adler;
                    adler += buf[8];  sum2 += adler;
                    adler += buf[9];  sum2 += adler;
                    adler += buf[10]; sum2 += adler;
                    adler += buf[11]; sum2 += adler;
                    adler += buf[12]; sum2 += adler;
                    adler += buf[13]; sum2 += adler;
                    adler += buf[14]; sum2 += adler;
                    adler += buf[15]; sum2 += adler;
                    buf += 16;
                } while (0 != --n);
                adler %= BASE;
                sum2 %= BASE;
            }

            /* do remaining bytes (less than NMAX, still just one modulo) */
            if (0 != len) {                  /* avoid modulos if none remaining */
                while (len >= 16) {
                    len -= 16;
                    adler += buf[0];  sum2 += adler;
                    adler += buf[1];  sum2 += adler;
                    adler += buf[2];  sum2 += adler;
                    adler += buf[3];  sum2 += adler;
                    adler += buf[4];  sum2 += adler;
                    adler += buf[5];  sum2 += adler;
                    adler += buf[6];  sum2 += adler;
                    adler += buf[7];  sum2 += adler;
                    adler += buf[8];  sum2 += adler;
                    adler += buf[9];  sum2 += adler;
                    adler += buf[10]; sum2 += adler;
                    adler += buf[11]; sum2 += adler;
                    adler += buf[12]; sum2 += adler;
                    adler += buf[13]; sum2 += adler;
                    adler += buf[14]; sum2 += adler;
                    adler += buf[15]; sum2 += adler;
                    buf += 16;
                }
                while (0 != len--) {
                    adler += *buf++;
                    sum2 += adler;
                }
                adler %= BASE;
                sum2 %= BASE;
            }

            /* return recombined sums */
            return adler | (sum2 << 16);
        }

        private uint m_adler = 1;
        public  uint Value { get { return m_adler; } }

        public void Update (byte[] buf, int pos, int len)
        {
            if (0 == len)
                return;
            unsafe
            {
                fixed (byte* ptr = &buf[pos])
                {
                    m_adler = Update (m_adler, ptr, len);
                }
            }
        }

        public unsafe uint Update (byte* buf, int len)
        {
            m_adler = Update (m_adler, buf, len);
            return m_adler;
        }
    }

    public class CheckedStream : Stream
    {
        Stream      m_stream;
        ICheckSum   m_checksum;

		public override bool  CanRead { get { return m_stream.CanRead; } }
		public override bool CanWrite { get { return m_stream.CanWrite; } }
		public override bool  CanSeek { get { return m_stream.CanSeek; } }
		public override long   Length { get { return m_stream.Length; } }

		public Stream  BaseStream { get { return m_stream; } }
		public uint CheckSumValue { get { return m_checksum.Value; } }

        public CheckedStream (Stream stream, ICheckSum algorithm)
        {
            m_stream = stream;
            m_checksum = algorithm;
        }

		public override int Read (byte[] buffer, int offset, int count)
		{
			int read = m_stream.Read (buffer, offset, count);
            if (read > 0)
                m_checksum.Update (buffer, offset, read);
			return read;
		}

		public override void Write (byte[] buffer, int offset, int count)
		{
			m_stream.Write (buffer, offset, count);
            m_checksum.Update (buffer, offset, count);
		}

		public override long Position
		{
			get { return m_stream.Position; }
			set { m_stream.Position = value; }
		}

		public override void SetLength (long value)
		{
			m_stream.SetLength (value);
		}

		public override long Seek (long offset, SeekOrigin origin)
		{
			return m_stream.Seek (offset, origin);
		}

		public override void Flush ()
		{
			m_stream.Flush();
		}
    }
}
