//! \file       Utility.cs
//! \date       Sat Jul 05 02:47:33 2014
//! \brief      utility class for GameRes assembly.
//

namespace GameRes.Utility
{
    public static class Binary
    {
        public static uint BigEndian (uint u)
        {
            return (u & 0xff) << 24 | (u & 0xff00) << 8 | (u & 0xff0000) >> 8 | (u & 0xff000000) >> 24;
        }
        public static int BigEndian (int i)
        {
            return (int)BigEndian ((uint)i);
        }
        public static ushort BigEndian (ushort u)
        {
            return (ushort)((u & 0xff) << 8 | (u & 0xff00) >> 8);
        }
        public static short BigEndian (short i)
        {
            return (short)BigEndian ((ushort)i);
        }
        public static ulong BigEndian (ulong u)
        {
            return (ulong)BigEndian((uint)(u & 0xffffffff)) << 32
                 | (ulong)BigEndian((uint)(u >> 32));
        }
        public static long BigEndian (long i)
        {
            return (long)BigEndian ((ulong)i);
        }

        public static bool AsciiEqual (byte[] name1, string name2)
        {
            return AsciiEqual (name1, 0, name2);
        }

        public static bool AsciiEqual (byte[] name1, int offset, string name2)
        {
            if (name1.Length-offset < name2.Length)
                return false;
            for (int i = 0; i < name2.Length; ++i)
                if ((char)name1[offset+i] != name2[i])
                    return false;
            return true;
        }
    }

    public static class LittleEndian
    {
        public static ushort ToUInt16 (byte[] value, int index)
        {
            return (ushort)(value[index] | value[index+1] << 8);
        }

        public static short ToInt16 (byte[] value, int index)
        {
            return (short)(value[index] | value[index+1] << 8);
        }

        public static uint ToUInt32 (byte[] value, int index)
        {
            return (uint)(value[index] | value[index+1] << 8 | value[index+2] << 16 | value[index+3] << 24);
        }

        public static int ToInt32 (byte[] value, int index)
        {
            return (int)ToUInt32 (value, index);
        }

        public static ulong ToUInt64 (byte[] value, int index)
        {
            return (ulong)ToUInt32 (value, index) | ((ulong)ToUInt32 (value, index+4) << 32);
        }

        public static long ToInt64 (byte[] value, int index)
        {
            return (long)ToUInt64 (value, index);
        }
    }

    public sealed class Crc32
    {
        /* Table of CRCs of all 8-bit messages. */
        private static readonly uint[] crc_table = InitializeTable();

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
        static uint UpdateCrc (uint crc, byte[] buf, int pos, int len)
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

    public sealed class Adler32
    {
        const uint BASE = 65521;      /* largest prime smaller than 65536 */
        const int  NMAX = 5552;

        public static uint Compute (byte[] buf, int pos, int len)
        {
            return Update (1, buf, pos, len);
        }

        private static uint Update (uint adler, byte[] buf, int pos, int len)
        {
            /* split Adler-32 into component sums */
            uint sum2 = (adler >> 16) & 0xffff;
            adler &= 0xffff;

            /* in case user likes doing a byte at a time, keep it fast */
            if (1 == len) {
                adler += buf[pos];
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
                    adler += buf[pos++];
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
                    adler += buf[pos];    sum2 += adler;
                    adler += buf[pos+1];  sum2 += adler;
                    adler += buf[pos+2];  sum2 += adler;
                    adler += buf[pos+3];  sum2 += adler;
                    adler += buf[pos+4];  sum2 += adler;
                    adler += buf[pos+5];  sum2 += adler;
                    adler += buf[pos+6];  sum2 += adler;
                    adler += buf[pos+7];  sum2 += adler;
                    adler += buf[pos+8];  sum2 += adler;
                    adler += buf[pos+9];  sum2 += adler;
                    adler += buf[pos+10]; sum2 += adler;
                    adler += buf[pos+11]; sum2 += adler;
                    adler += buf[pos+12]; sum2 += adler;
                    adler += buf[pos+13]; sum2 += adler;
                    adler += buf[pos+14]; sum2 += adler;
                    adler += buf[pos+15]; sum2 += adler;
                    pos += 16;
                } while (0 != --n);
                adler %= BASE;
                sum2 %= BASE;
            }

            /* do remaining bytes (less than NMAX, still just one modulo) */
            if (0 != len) {                  /* avoid modulos if none remaining */
                while (len >= 16) {
                    len -= 16;
                    adler += buf[pos];    sum2 += adler;
                    adler += buf[pos+1];  sum2 += adler;
                    adler += buf[pos+2];  sum2 += adler;
                    adler += buf[pos+3];  sum2 += adler;
                    adler += buf[pos+4];  sum2 += adler;
                    adler += buf[pos+5];  sum2 += adler;
                    adler += buf[pos+6];  sum2 += adler;
                    adler += buf[pos+7];  sum2 += adler;
                    adler += buf[pos+8];  sum2 += adler;
                    adler += buf[pos+9];  sum2 += adler;
                    adler += buf[pos+10]; sum2 += adler;
                    adler += buf[pos+11]; sum2 += adler;
                    adler += buf[pos+12]; sum2 += adler;
                    adler += buf[pos+13]; sum2 += adler;
                    adler += buf[pos+14]; sum2 += adler;
                    adler += buf[pos+15]; sum2 += adler;
                    pos += 16;
                }
                while (0 != len--) {
                    adler += buf[pos++];
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
            m_adler = Update (m_adler, buf, pos, len);
        }
    }
}
