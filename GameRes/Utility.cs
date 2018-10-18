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

using System.Collections.Generic;
using System.Text;

namespace GameRes.Utility
{
    public static class Binary
    {
        public static uint BigEndian (uint u)
        {
            return u << 24 | (u & 0xff00) << 8 | (u & 0xff0000) >> 8 | u >> 24;
        }
        public static int BigEndian (int i)
        {
            return (int)BigEndian ((uint)i);
        }
        public static ushort BigEndian (ushort u)
        {
            return (ushort)(u << 8 | u >> 8);
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

        /// <summary>
        /// Check if sequence of ASCII characters in array <paramref name="name1"/> is equal to string <paramref name="name2"/>.
        /// This methods avoids costly construction of the string object from byte array for a mere purpose of
        /// comparison.
        /// </summary>
        public static bool AsciiEqual (byte[] name1, int offset, string name2)
        {
            return name1.AsciiEqual (offset, name2);
        }

        /// <summary>
        /// Copy potentially overlapping sequence of <paramref name="count"/> bytes in array
        /// <paramref name="data"/> from <paramref name="src"/> to <paramref name="dst"/>.
        /// If destination offset resides within source region then sequence will repeat itself.  Widely used
        /// in various compression techniques.
        /// </summary>
        public static void CopyOverlapped (byte[] data, int src, int dst, int count)
        {
            if (dst > src)
            {
                while (count > 0)
                {
                    int preceding = System.Math.Min (dst - src, count);
                    System.Buffer.BlockCopy (data, src, data, dst, preceding);
                    dst += preceding;
                    count -= preceding;
                }
            }
            else
            {
                System.Buffer.BlockCopy (data, src, data, dst, count);
            }
        }

        /// <summary>
        /// Extract null-terminated string (a "C string") from array <paramref name="data"/> starting
        /// at offset <paramref name="index"/> up to <paramref name="length_limit"/> bytes long, stored in
        /// encoding <paramref name="enc"/>.
        /// </summary>
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

        public static string GetCString (byte[] data, int index)
        {
            return GetCString (data, index, data.Length - index, Encodings.cp932);
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

        public static ulong RotR (ulong v, int count)
        {
            count &= 0x3F;
            return v >> count | v << (64-count);
        }

        public static ulong RotL (ulong v, int count)
        {
            count &= 0x3F;
            return v << count | v >> (64-count);
        }

        public static byte RotByteR (byte v, int count)
        {
            count &= 7;
            return (byte)(v >> count | v << (8-count));
        }

        public static byte RotByteL (byte v, int count)
        {
            count &= 7;
            return (byte)(v << count | v >> (8-count));
        }
    }

    public static class BigEndian
    {
        public static ushort ToUInt16<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (ushort)(value[index] << 8 | value[index+1]);
        }

        public static short ToInt16<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (short)(value[index] << 8 | value[index+1]);
        }

        public static uint ToUInt32<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (uint)(value[index] << 24 | value[index+1] << 16 | value[index+2] << 8 | value[index+3]);
        }

        public static int ToInt32<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (int)ToUInt32 (value, index);
        }

        public static void Pack (ushort value, byte[] buf, int index)
        {
            buf[index]   = (byte)(value >> 8);
            buf[index+1] = (byte)(value);
        }

        public static void Pack (uint value, byte[] buf, int index)
        {
            buf[index]   = (byte)(value >> 24);
            buf[index+1] = (byte)(value >> 16);
            buf[index+2] = (byte)(value >> 8);
            buf[index+3] = (byte)(value);
        }

        public static void Pack (ulong value, byte[] buf, int index)
        {
            Pack ((uint)(value >> 32), buf, index);
            Pack ((uint)value, buf, index+4);
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

    public static class LittleEndian
    {
        public static ushort ToUInt16<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (ushort)(value[index] | value[index+1] << 8);
        }

        public static short ToInt16<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (short)(value[index] | value[index+1] << 8);
        }

        public static uint ToUInt32<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (uint)(value[index] | value[index+1] << 8 | value[index+2] << 16 | value[index+3] << 24);
        }

        public static int ToInt32<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (int)ToUInt32 (value, index);
        }

        public static ulong ToUInt64<TArray> (TArray value, int index) where TArray : IList<byte>
        {
            return (ulong)ToUInt32 (value, index) | ((ulong)ToUInt32 (value, index+4) << 32);
        }

        public static long ToInt64<TArray> (TArray value, int index) where TArray : IList<byte>
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
