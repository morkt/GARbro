//! \file       BinaryStream.cs
//! \date       Sun Mar 13 00:37:07 2016
//! \brief      Wrapper around IO.Stream to read binary streams.
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
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes
{
    public interface IBinaryStream
    {
        /// <summary>
        /// Name of the stream (could be name of the underlying file) or an empty string.
        /// </summary>
        string     Name { get; }
        /// <summary>
        /// First 4 bytes of the stream as a little-endian integer.
        /// </summary>
        uint  Signature { get; }
        Stream AsStream { get; }
        long     Length { get; }
        long   Position { get; set; }

        long    Seek (long offset, SeekOrigin origin);

        int     Read (byte[] buffer, int offset, int count);
        int     ReadByte ();

        /// <summary>
        /// Read next byte from stream without advancing stream position.
        /// </summary>
        /// <returns>Next byte, or -1 if end of stream is reached.</returns>
        int     PeekByte ();

        sbyte   ReadSByte ();
        short   ReadInt16 ();
        ushort  ReadUInt16 ();
        int     ReadInt24 ();
        int     ReadInt32 ();
        uint    ReadUInt32 ();
        long    ReadInt64 ();
        ulong   ReadUInt64 ();

        /// <summary>
        /// Read first <paramref="size"/> bytes of stream and return them in a copy-on-write array.
        /// </summary>
        CowArray<byte> ReadHeader (int size);

        /// <summary>
        /// Read zero-terminated string at most of <paramref name="length"/> bytes length from stream in
        /// specified encoding.
        /// Advances stream position forward by <paramref name="length"/> bytes, regardless of where zero byte
        /// was encountered.
        /// </summary>
        string ReadCString (int length, Encoding enc);
        /// <summary>
        /// Does ReadCString with CP932 encoding.
        /// </summary>
        string ReadCString (int length);

        /// <summary>
        /// Read zero-terminated string from stream in specefied encoding.
        /// Stream is positioned after a zero byte that terminates a string, or at end of stream, whichever
        /// comes first.
        /// </summary>
        string ReadCString (Encoding enc);
        /// <summary>
        /// Does ReadCString with CP932 encoding.
        /// </summary>
        string ReadCString ();

        /// <summary>
        /// Read <paramref name="count"/> bytes starting from the current position into array and return it.
        /// If there's less than <paramref name="count"/> bytes left in the stream, array size will be
        /// smaller than count.
        /// </summary>
        byte[] ReadBytes (int count);
    }

    /// <summary>
    /// Array segment with copy-on-write semantics.
    /// </summary>
    public struct CowArray<T> : IReadOnlyList<T>
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

        public int  Count { get { return m_count; } }
        public int Length { get { return Count; } }
        public T this[int pos]
        {
            get { return m_source[m_offset+pos]; }
            set
            {
                if (!m_own_copy)
                {
                    m_source = ToArray();
                    m_offset = 0;
                    m_own_copy = true;
                }
                m_source[pos] = value;
            }
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
    }

    public static class CowByteArray
    {
        public static ushort ToUInt16 (this CowArray<byte> arr, int index)
        {
            return (ushort)(arr[index] | arr[index+1] << 8);
        }

        public static short ToInt16 (this CowArray<byte> arr, int index)
        {
            return (short)(arr[index] | arr[index+1] << 8);
        }

        public static int ToInt24 (this CowArray<byte> arr, int index)
        {
            return arr[index] | arr[index+1] << 8 | arr[index+2] << 16;
        }

        public static uint ToUInt32 (this CowArray<byte> arr, int index)
        {
            return (uint)(arr[index] | arr[index+1] << 8 | arr[index+2] << 16 | arr[index+3] << 24);
        }

        public static int ToInt32 (this CowArray<byte> arr, int index)
        {
            return (int)ToUInt32 (arr, index);
        }

        public static ulong ToUInt64 (this CowArray<byte> arr, int index)
        {
            return (ulong)ToUInt32 (arr, index) | ((ulong)ToUInt32 (arr, index+4) << 32);
        }

        public static long ToInt64 (this CowArray<byte> arr, int index)
        {
            return (long)ToUInt64 (arr, index);
        }
    }

    public class BinaryStream : Stream, IBinaryStream
    {
        Stream      m_source;
        bool        m_should_dispose;
        byte[]      m_buffer;
        int         m_buffer_pos;
        int         m_buffer_end;
        Lazy<uint>  m_signature;
        byte[]      m_header;
        int         m_header_size;

        public string     Name { get; private set; }
        public uint  Signature { get { return m_signature.Value; } }
        public Stream AsStream { get { return this; } }

        public BinaryStream (Stream input, bool leave_open = false) : this (input, "", leave_open)
        {
        }

        public BinaryStream (Stream input, string name, bool leave_open = false)
        {
            if (null == name)
                name = "";
            m_source = input;
            m_should_dispose = !leave_open;
            m_buffer = new byte[0x10];
            m_buffer_pos = 0;
            m_buffer_end = 0;
            m_signature = new Lazy<uint> (ReadSignature);
            m_header_size = 0;
            Name = name;
        }

        uint ReadSignature ()
        {
            if (m_header_size >= 4)
            {
                return LittleEndian.ToUInt32 (m_header, 0);
            }
            var pos = Position;
            if (pos != 0)
                Position = 0;
            uint signature = ReadUInt32();
            Position = pos;
            return signature;
        }

        public CowArray<byte> ReadHeader (int size)
        {
            if (m_header_size < size)
            {
                if (null == m_header || m_header.Length < size)
                    Array.Resize (ref m_header, (size + 0xF) & ~0xF);
                Position = m_header_size;
                m_header_size += Read (m_header, m_header_size, size - m_header_size);
            }
            size = Math.Min (size, m_header_size);
            Position = size;
            return new CowArray<byte> (m_header, 0, size);
        }

        private int FillBuffer (int count)
        {
            int cached = m_buffer_end - m_buffer_pos;
            if (count <= cached)
                return count;
            if (m_buffer_pos + count > m_buffer.Length)
            {
                if (cached + count > m_buffer.Length)
                {
                    var copy = new byte[(count + 0xF) & ~0xF];
                    if (cached > 0)
                        Buffer.BlockCopy (m_buffer, m_buffer_pos, copy, 0, cached);
                    m_buffer = copy;
                }
                else if (cached > 0)
                {
                    Buffer.BlockCopy (m_buffer, m_buffer_pos, m_buffer, 0, cached);
                }
                m_buffer_pos = 0;
                m_buffer_end = cached;
            }
            m_buffer_end += m_source.Read (m_buffer, m_buffer_end, count - cached);
            return Math.Min (count, m_buffer_end - m_buffer_pos);
        }

        public int PeekByte ()
        {
            if (1 != FillBuffer (1))
                return -1;
            return m_buffer[m_buffer_pos];
        }

        public sbyte ReadSByte ()
        {
            if (1 != FillBuffer (1))
                throw new EndOfStreamException();
            return (sbyte)m_buffer[m_buffer_pos++];
        }

        public short ReadInt16 ()
        {
            if (2 != FillBuffer (2))
                throw new EndOfStreamException();
            short v = LittleEndian.ToInt16 (m_buffer, m_buffer_pos);
            m_buffer_pos += 2;
            return v;
        }

        public ushort ReadUInt16 ()
        {
            return (ushort)ReadInt16();
        }

        public int ReadInt24 ()
        {
            if (3 != FillBuffer (3))
                throw new EndOfStreamException();
            int v = LittleEndian.ToUInt16 (m_buffer, m_buffer_pos);
            v |= m_buffer[m_buffer_pos+2] << 16;
            m_buffer_pos += 3;
            return v;
        }

        public int ReadInt32 ()
        {
            if (4 != FillBuffer (4))
                throw new EndOfStreamException();
            int v = LittleEndian.ToInt32 (m_buffer, m_buffer_pos);
            m_buffer_pos += 4;
            return v;
        }

        public uint ReadUInt32 ()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64 ()
        {
            if (8 != FillBuffer (8))
                throw new EndOfStreamException();
            long v = LittleEndian.ToInt64 (m_buffer, m_buffer_pos);
            m_buffer_pos += 8;
            return v;
        }

        public ulong ReadUInt64 ()
        {
            return (ulong)ReadInt64();
        }

        public string ReadCString (int length)
        {
            return ReadCString (length, Encodings.cp932);
        }

        public string ReadCString (int length, Encoding enc)
        {
            length = FillBuffer (length);
            int i;
            for (i = 0; i < length; ++i)
                if (0 == m_buffer[m_buffer_pos+i])
                    break;
            string s = enc.GetString (m_buffer, m_buffer_pos, i);
            m_buffer_pos += length;
            return s;
        }

        public string ReadCString ()
        {
            return ReadCString (Encodings.cp932);
        }

        public string ReadCString (Encoding enc)
        {
            int count = 0;
            int cached;
            for (;;)
            {
                cached = FillBuffer (count+1);
                if (cached < count+1)
                    break;
                if (0 == m_buffer[m_buffer_pos+count])
                    break;
                ++count;
            }
            var s = enc.GetString (m_buffer, m_buffer_pos, count);
            m_buffer_pos += Math.Min (count+1, cached);
            return s;
        }

        public byte[] ReadBytes (int count)
        {
            var buffer = new byte[count];
            int pos = 0;
            int cached = m_buffer_end - m_buffer_pos;
            if (cached > 0)
            {
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, 0, cached);
                pos = cached;
                m_buffer_end = m_buffer_pos = 0;
                count -= cached;
            }
            pos += m_source.Read (buffer, pos, count);
            if (pos < buffer.Length)
            {
                var copy = new byte[pos];
                Buffer.BlockCopy (buffer, 0, copy, 0, copy.Length);
                buffer = copy;
            }
            return buffer;
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return m_source.CanRead; } }
        public override bool CanSeek  { get { return m_source.CanSeek; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_source.Length; } }
        public override long Position
        {
            get { return m_source.Position - (m_buffer_end - m_buffer_pos); }
            set
            {
                m_source.Position = value;
                m_buffer_end = m_buffer_pos = 0;
            }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            int cached = Math.Min (m_buffer_end - m_buffer_pos, count);
            if (cached > 0)
            {
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, cached);
                m_buffer_pos += cached;
                offset += cached;
                count -= cached;
                read += cached;
            }
            if (count > 0)
            {
                read += m_source.Read (buffer, offset, count);
            }
            return read;
        }

        public override int ReadByte ()
        {
            int b;
            if (m_buffer_pos < m_buffer_end)
                b = m_buffer[m_buffer_pos++];
            else
                b = m_source.ReadByte();
            return b;
        }

        public override void Flush()
        {
            m_source.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Current == origin)
                offset -= m_buffer_end - m_buffer_pos;
            m_buffer_end = m_buffer_pos = 0;
            return m_source.Seek (offset, origin);
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("BinaryStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("BinaryStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("BinaryStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (m_should_dispose)
                    m_source.Dispose();
            }
            _disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}
