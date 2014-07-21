//! \file       adler32.cs
//! \date       Mon Jul 21 11:19:54 2014
//! \brief      compute adler32 checksum
//

using System.IO;
using System;

class Adler
{
    public static void Main (string[] args)
    {
        if (args.Length < 1)
            return;
        try
        {
            uint adler = 1;
            using (var input = File.Open (args[0], FileMode.Open, FileAccess.Read))
            {
                var buf = new byte[65536];
                for (;;)
                {
                    int read = input.Read (buf, 0, buf.Length);
                    if (0 == read)
                        break;
                    adler = Adler32.Update (adler, buf, 0, read);
                }
            }
            Console.WriteLine ("{0} => {1:X8}", args[0], adler);
        }
        catch (Exception X)
        {
            Console.Error.WriteLine (X.Message);
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

        public static uint Update (uint adler, byte[] buf, int pos, int len)
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
