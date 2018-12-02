//! \file       ArcStream.cs
//! \date       Thu Mar 30 03:19:41 2017
//! \brief      Stream on top of the memory-mapped view.
//
// Copyright (C) 2014-2017 by morkt
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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes
{
    public class ArcViewStream : Stream, IBinaryStream
    {
        private readonly ArcView.Frame  m_view;
        private readonly long           m_start;
        private readonly long           m_size;
        private long                    m_position;
        private byte[]                  m_buffer;
        private int                     m_buffer_pos;   // read position within buffer
        private int                     m_buffer_len;   // length of bytes read in buffer

        private const int DefaultBufferSize = 0x1000;
        private const uint MaxFrameSize = 0x1000000; // 16MB

        public string     Name { get; set; }
        public uint  Signature { get { return ReadSignature(); } }
        public Stream AsStream { get { return this; } }

        public override bool CanRead  { get { return !disposed; } }
        public override bool CanSeek  { get { return !disposed; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_size; } }
        public override long Position
        {
            get { return m_position + (m_buffer_pos - m_buffer_len); }
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException ("value", "Stream position is out of range.");
                var buffer_start = m_position - m_buffer_len;
                if (m_buffer_pos != m_buffer_len && value >= buffer_start && value < m_position)
                {
                    m_buffer_pos = (int)(value - buffer_start);
                }
                else
                {
                    m_position = value;
                    m_buffer_pos = m_buffer_len = 0;
                }
            }
        }

        public ArcViewStream (ArcView file)
        {
            m_view = file.CreateFrame();
            m_start = 0;
            m_size = file.MaxOffset;
            m_position = 0;
            Name = file.Name;
        }

        public ArcViewStream (ArcView.Frame view, string name = null)
        {
            m_view = view;
            m_start = m_view.Offset;
            m_size = m_view.Reserved;
            m_position = 0;
            Name = name ?? "";
        }

        public ArcViewStream (ArcView file, long offset, uint size, string name = null)
        {
            m_view = new ArcView.Frame (file, offset, Math.Min (size, MaxFrameSize));
            m_start = offset;
            m_size = size;
            m_position = 0;
            Name = name;
        }

        public ArcViewStream (ArcView.Frame view, long offset, uint size, string name = null)
        {
            m_view = view;
            m_start = offset;
            m_size = Math.Min (size, m_view.Reserve (offset, size));
            m_position = 0;
            Name = name ?? "";
        }

        /// <summary>
        /// Read stream signature (first 4 bytes) without altering current read position.
        /// </summary>
        public uint ReadSignature ()
        {
            return m_view.ReadUInt32 (m_start);
        }

        byte[]      m_header;
        int         m_header_size;

        public CowArray<byte> ReadHeader (int size)
        {
            if (m_header_size < size)
            {
                if (null == m_header || m_header.Length < size)
                    Array.Resize (ref m_header, (size + 0xF) & ~0xF);
                long position = m_start + m_header_size;
                m_header_size += m_view.Read (position, m_header, m_header_size, (uint)(size - m_header_size));
            }
            if (size > m_header_size)
            {
                Position = m_header_size;
                throw new EndOfStreamException();
            }
            Position = size;
            return new CowArray<byte> (m_header, 0, size);
        }

        private void RefillBuffer ()
        {
            if (null == m_buffer)
                m_buffer = new byte[DefaultBufferSize];
            uint length = (uint)Math.Min (m_size - m_position, m_buffer.Length);
            m_buffer_len = m_view.Read (m_start + m_position, m_buffer, 0, length);
            m_position += m_buffer_len;
            m_buffer_pos = 0;
        }

        private void FlushBuffer ()
        {
            if (m_buffer_len != 0)
            {
                m_position += m_buffer_pos - m_buffer_len;
                m_buffer_pos = m_buffer_len = 0;
            }
        }

        private void EnsureAvailable (int length)
        {
            if (m_buffer_pos + length > m_buffer_len)
            {
                FlushBuffer();
                if (m_position + length > m_size)
                    throw new EndOfStreamException();
                RefillBuffer();
            }
        }

        private int ReadFromBuffer (byte[] array, int offset, int count)
        {
            int available = Math.Min (m_buffer_len - m_buffer_pos, count);
            if (available > 0)
            {
                Buffer.BlockCopy (m_buffer, m_buffer_pos, array, offset, available);
                m_buffer_pos += available;
            }
            return available;
        }

        public int PeekByte ()
        {
            if (m_buffer_pos == m_buffer_len)
                RefillBuffer();
            if (m_buffer_pos == m_buffer_len)
                return -1;
            return m_buffer[m_buffer_pos];
        }

        public override int ReadByte ()
        {
            int b = PeekByte();
            if (-1 != b)
                ++m_buffer_pos;
            return b;
        }

        public sbyte ReadInt8 ()
        {
            int b = ReadByte();
            if (-1 == b)
                throw new EndOfStreamException();
            return (sbyte)b;
        }

        public byte ReadUInt8 ()
        {
            return (byte)ReadInt8();
        }

        public short ReadInt16 ()
        {
            EnsureAvailable (2);
            var v = m_buffer.ToInt16 (m_buffer_pos);
            m_buffer_pos += 2;
            return v;
        }

        public ushort ReadUInt16 ()
        {
            return (ushort)ReadInt16();
        }

        public int ReadInt24 ()
        {
            EnsureAvailable (3);
            int v = m_buffer.ToInt24 (m_buffer_pos);
            m_buffer_pos += 3;
            return v;
        }

        public int ReadInt32 ()
        {
            EnsureAvailable (4);
            int v = m_buffer.ToInt32 (m_buffer_pos);
            m_buffer_pos += 4;
            return v;
        }

        public uint ReadUInt32 ()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64 ()
        {
            EnsureAvailable (8);
            var v = m_buffer.ToInt64 (m_buffer_pos);
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
            if (m_buffer_pos == m_buffer_len && length <= DefaultBufferSize)
                RefillBuffer();
            if (m_buffer_pos + length <= m_buffer_len)
            {
                // whole string fit into buffer
                var str = Binary.GetCString (m_buffer, m_buffer_pos, length, enc);
                m_buffer_pos += length;
                return str;
            }
            else if (length > DefaultBufferSize)
            {
                // requested string length is larger than internal buffer size
                var string_buffer = ReadBytes (length);
                return Binary.GetCString (string_buffer, 0, string_buffer.Length, enc);
            }
            else
            {
                int available = m_buffer_len - m_buffer_pos;
                if (available > 0 && m_buffer_pos != 0)
                    Buffer.BlockCopy (m_buffer, m_buffer_pos, m_buffer, 0, available);
                int count = (int)Math.Min (m_buffer.Length - available, m_size - m_position);
                if (count > 0)
                {
                    int read = m_view.Read (m_start + m_position, m_buffer, available, (uint)count);
                    m_position += read;
                    available += read;
                }
                m_buffer_len = available;
                m_buffer_pos = Math.Min (length, m_buffer_len);
                return Binary.GetCString (m_buffer, 0, m_buffer_pos, enc);
            }
        }

        public string ReadCString ()
        {
            return ReadCString (Encodings.cp932);
        }

        public string ReadCString (Encoding enc)
        {
            if (m_buffer_pos == m_buffer_len)
                RefillBuffer();
            int available = m_buffer_len - m_buffer_pos;
            if (0 == available)
                return string.Empty;

            int zero = Array.IndexOf<byte> (m_buffer, 0, m_buffer_pos, available);
            if (zero != -1)
            {
                // null byte found within buffer
                var str = enc.GetString (m_buffer, m_buffer_pos, zero - m_buffer_pos);
                m_buffer_pos = zero+1;
                return str;
            }
            // underlying view includes whole stream
            if (m_view.Offset <= m_start && m_view.Offset + m_view.Reserved >= m_start + m_size)
                return ReadCStringUnsafe (enc, available);

            var string_buf = new byte[Math.Max (0x20, available * 2)];
            ReadFromBuffer (string_buf, 0, available);
            int size = available;
            for (;;)
            {
                int b = ReadByte();
                if (-1 == b || 0 == b)
                    break;
                if (string_buf.Length == size)
                {
                    Array.Resize (ref string_buf, checked(size*3/2));
                }
                string_buf[size++] = (byte)b;
            }
            return enc.GetString (string_buf, 0, size);
        }

        private unsafe string ReadCStringUnsafe (Encoding enc, int skip_bytes = 0)
        {
            Debug.Assert (m_view.Offset + m_view.Reserved >= m_start + m_size);
            FlushBuffer();
            using (var ptr = m_view.GetPointer())
            {
                byte* s = ptr.Value + (m_start - m_view.Offset + m_position);
                int view_length = (int)(m_size - m_position);
                int string_length = Math.Min (skip_bytes, view_length);
                while (string_length < view_length && 0 != s[string_length])
                {
                    ++string_length;
                }
                m_position += string_length;
                if (string_length < view_length)
                    ++m_position;
                return new string ((sbyte*)s, 0, string_length, enc);
            }
        }

        public byte[] ReadBytes (int count)
        {
            if (m_buffer_pos + count <= m_buffer_len && m_buffer_len != 0)
            {
                var data = new CowArray<byte> (m_buffer, m_buffer_pos, count).ToArray();
                m_buffer_pos += count;
                return data;
            }
            var current_pos = Position;
            if (0 == count || current_pos >= m_size)
                return Array.Empty<byte>();
            var bytes = m_view.ReadBytes (m_start+current_pos, (uint)Math.Min (count, m_size - current_pos));
            Position = current_pos + bytes.Length;
            return bytes;
        }

        #region System.IO.Stream methods
        public override void Flush()
        {
            FlushBuffer();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            switch (origin)
            {
            case SeekOrigin.Current:    offset += Position; break;
            case SeekOrigin.End:        offset += m_size; break;
            }
            Position = offset;
            return offset;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("GameRes.ArcStream.SetLength method is not supported");
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read_from_buffer = ReadFromBuffer (buffer, offset, count);
            offset += read_from_buffer;
            count -= read_from_buffer;
            if (0 == count || m_position >= m_size)
                return read_from_buffer;
            if (count < DefaultBufferSize)
            {
                RefillBuffer();
                count = Math.Min (count, m_buffer_len);
                Buffer.BlockCopy (m_buffer, m_buffer_pos, buffer, offset, count);
                m_buffer_pos += count;
                return read_from_buffer + count;
            }
            else
            {
                uint view_count = (uint)Math.Min (count, m_size - m_position);
                int read_from_view = m_view.Read (m_start + m_position, buffer, offset, view_count);
                m_position += read_from_view;
                return read_from_buffer + read_from_view;
            }
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("GameRes.ArcStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException("GameRes.ArcStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_view.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
