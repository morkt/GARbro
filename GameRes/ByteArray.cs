//! \file       ByteArray.cs
//! \date       Sun Oct 16 06:25:52 2016
//! \brief      specialized collection for copy-on-write byte arrays.
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

using System;
using System.Collections;
using System.Collections.Generic;
using GameRes.Utility;

namespace GameRes
{
    /// <summary>
    /// Array segment with copy-on-write semantics.
    /// </summary>
    public struct CowArray<T> : IList<T>
    {
        T[]             m_source;
        int             m_offset;
        int             m_count;
        bool            m_own_copy;

        public CowArray (T[] src) : this (src, 0, src.Length)
        {
        }

        public CowArray (T[] src, int start, int length)
        {
            m_source = src;
            m_offset = start;
            m_count = length;
            m_own_copy = false;
        }

        public int       Count { get { return m_count; } }
        public int      Length { get { return Count; } }
        public bool IsReadOnly { get { return true; } }
        public T this[int pos]
        {
            get { return m_source[m_offset+pos]; }
            set
            {
                if (!m_own_copy)
                {
                    Reclaim();
                }
                m_source[pos] = value;
            }
        }

        public int IndexOf (T item)
        {
            int i = Array.IndexOf<T> (m_source, item, m_offset, m_count);
            if (-1 == i)
                return i;
            return i - m_offset;
        }

        public bool Contains (T item)
        {
            return Array.IndexOf<T> (m_source, item, m_offset, m_count) != -1;
        }

        public void CopyTo (T[] arr, int dst)
        {
            Array.Copy (m_source, m_offset, arr, dst, m_count);
        }

        public IEnumerator<T> GetEnumerator ()
        {
            for (int i = 0; i < m_count; ++i)
                yield return m_source[m_offset + i];
        }

        IEnumerator IEnumerable.GetEnumerator ()
        {
            return GetEnumerator();
        }

        public T[] ToArray ()
        {
            if (m_own_copy)
                return m_source;
            var copy = new T[m_count];
            Array.Copy (m_source, m_offset, copy, 0, m_count);
            return copy;
        }

        internal void Reclaim ()
        {
            m_source = ToArray();
            m_offset = 0;
            m_own_copy = true;
        }

        #region Not supported methods
        public void Insert (int index, T item)
        {
            throw new NotSupportedException();
        }

        public bool Remove (T item)
        {
            throw new NotSupportedException();
        }

        public void RemoveAt (int index)
        {
            throw new NotSupportedException();
        }

        public void Add (T item)
        {
            throw new NotSupportedException();
        }

        public void Clear ()
        {
            throw new NotSupportedException();
        }
        #endregion
    }

    public static class ByteArrayExt
    {
        public static ushort ToUInt16<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (ushort)(arr[index] | arr[index+1] << 8);
        }

        public static short ToInt16<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (short)(arr[index] | arr[index+1] << 8);
        }

        public static int ToInt24<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return arr[index] | arr[index+1] << 8 | arr[index+2] << 16;
        }

        public static uint ToUInt32<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (uint)(arr[index] | arr[index+1] << 8 | arr[index+2] << 16 | arr[index+3] << 24);
        }

        public static int ToInt32<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (int)ToUInt32 (arr, index);
        }

        public static ulong ToUInt64<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (ulong)ToUInt32 (arr, index) | ((ulong)ToUInt32 (arr, index+4) << 32);
        }

        public static long ToInt64<TArray> (this TArray arr, int index) where TArray : IList<byte>
        {
            return (long)ToUInt64 (arr, index);
        }

        public static bool AsciiEqual<TArray> (this TArray arr, int index, string str) where TArray : IList<byte>
        {
            if (arr.Count-index < str.Length)
                return false;
            for (int i = 0; i < str.Length; ++i)
                if ((char)arr[index+i] != str[i])
                    return false;
            return true;
        }

        public static bool AsciiEqual<TArray> (this TArray arr, string str) where TArray : IList<byte>
        {
            return arr.AsciiEqual (0, str);
        }

        public static string GetCString (this CowArray<byte> arr, int index, int length_limit)
        {
            arr.Reclaim();
            return Binary.GetCString (arr.ToArray(), index, length_limit);
        }
    }
}
