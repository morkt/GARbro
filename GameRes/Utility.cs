//! \file       Utility.cs
//! \date       Sat Jul 05 02:47:33 2014
//! \brief      utility classes for GameRes assembly.
//
// Copyright (C) 2014 by morkt
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
using System.Text;

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

        public static void CopyOverlapped (byte[] data, int src, int dst, int count)
        {
            int preceding = dst-src;
            if (preceding > 0)
            {
                while (count > 0)
                {
                    if (preceding > count)
                        preceding = count;
                    System.Buffer.BlockCopy (data, src, data, dst, preceding);
                    src = dst;
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                System.Buffer.BlockCopy (data, src, data, dst, count);
            }
        }

        public static string GetCString (byte[] data, int index, int length_limit, Encoding enc)
        {
            int name_length = 0;
            while (name_length < length_limit && 0 != data[index+name_length])
                name_length++;
            return enc.GetString (data, index, name_length);
        }

        public static string GetCString (byte[] data, int index, int length_limit)
        {
            return GetCString (data, index, length_limit, Encodings.cp932);
        }

        public static uint RotR (uint v, int count)
        {
            count &= 0x1F;
            return v >> count | v << (32-count);
        }

        public static uint RotL (uint v, int count)
        {
            count &= 0x1F;
            return v << count | v >> (32-count);
        }
    }

    public static class BigEndian
    {
        public static ushort ToUInt16 (byte[] value, int index)
        {
            return (ushort)(value[index] << 8 | value[index+1]);
        }

        public static short ToInt16 (byte[] value, int index)
        {
            return (short)(value[index] << 8 | value[index+1]);
        }

        public static uint ToUInt32 (byte[] value, int index)
        {
            return (uint)(value[index] << 24 | value[index+1] << 16 | value[index+2] << 8 | value[index+3]);
        }

        public static int ToInt32 (byte[] value, int index)
        {
            return (int)ToUInt32 (value, index);
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

        public static void Pack (ushort value, byte[] buf, int index)
        {
            buf[index]   = (byte)(value);
            buf[index+1] = (byte)(value >> 8);
        }

        public static void Pack (uint value, byte[] buf, int index)
        {
            buf[index]   = (byte)(value);
            buf[index+1] = (byte)(value >> 8);
            buf[index+2] = (byte)(value >> 16);
            buf[index+3] = (byte)(value >> 24);
        }

        public static void Pack (ulong value, byte[] buf, int index)
        {
            Pack ((uint)value, buf, index);
            Pack ((uint)(value >> 32), buf, index+4);
        }

        public static void Pack (short value, byte[] buf, int index)
        {
            Pack ((ushort)value, buf, index);
        }

        public static void Pack (int value, byte[] buf, int index)
        {
            Pack ((uint)value, buf, index);
        }

        public static void Pack (long value, byte[] buf, int index)
        {
            Pack ((ulong)value, buf, index);
        }
    }

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

    public class AsciiString
    {
        public byte[] Value { get; set; }
        public int   Length { get { return Value.Length; } }

        public AsciiString (int size)
        {
            Value = new byte[size];
        }

        public AsciiString (byte[] str)
        {
            Value = str;
        }

        public AsciiString (string str)
        {
            Value = Encoding.ASCII.GetBytes (str);
        }

        public override string ToString ()
        {
            return Encoding.ASCII.GetString (Value);
        }

        public override bool Equals (object o)
        {
            if (null == o)
                return false;
            var a = o as AsciiString;
            if (null == (object)a)
                return false;
            return this == a;
        }

        public override int GetHashCode ()
        {
            int hash = 5381;
            for (int i = 0; i < Value.Length; ++i)
            {
                hash = ((hash << 5) + hash) ^ Value[i];
            }
            return hash ^ (hash * 1566083941);;
        }

        public static bool operator== (AsciiString a, AsciiString b)
        {
            if (ReferenceEquals (a, b))
                return true;
            if (null == (object)a || null == (object)b)
                return false;
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; ++i)
                if (a.Value[i] != b.Value[i])
                    return false;
            return true;
        }

        public static bool operator!= (AsciiString a, AsciiString b)
        {
            return !(a == b);
        }

        public static bool operator== (AsciiString a, string b)
        {
            return Binary.AsciiEqual (a.Value, b);
        }

        public static bool operator!= (AsciiString a, string b)
        {
            return !(a == b);
        }

        public static bool operator== (string a, AsciiString b)
        {
            return b == a;
        }

        public static bool operator!= (string a, AsciiString b)
        {
            return !(b == a);
        }
    }

    public interface IDataUnpacker
    {
        byte[] Data { get; }
        void Unpack ();
    }
}
