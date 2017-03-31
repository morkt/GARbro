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

namespace GameRes
{
    public class ArcViewStream : Stream, IBinaryStream
    {
        private readonly ArcView.Frame  m_view;
        private readonly long           m_start;
        private readonly long           m_size;
        private long                    m_position;

        public string     Name { get; set; }
        public uint  Signature { get { return ReadSignature(); } }
        public Stream AsStream { get { return this; } }

        public override bool CanRead  { get { return !disposed; } }
        public override bool CanSeek  { get { return !disposed; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_size; } }
        public override long Position
        {
            get { return m_position; }
            set {
                if (value < 0)
                    throw new ArgumentOutOfRangeException ("value", "Stream position is out of range.");
                m_position = value;
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
            : this (new ArcView.Frame (file, offset, size), name)
        {
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

        private void EnsureAvailable (int length)
        {
            if (m_position + length > m_size)
                throw new EndOfStreamException();
        }

        public int PeekByte ()
        {
            if (m_position >= m_size)
                return -1;
             return m_view.ReadByte (m_start+m_position);
        }

        public override int ReadByte ()
        {
            int b = PeekByte();
            if (-1 != b)
                ++m_position;
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
            var v = m_view.ReadInt16 (m_start+m_position);
            m_position += 2;
            return v;
        }

        public ushort ReadUInt16 ()
        {
            return (ushort)ReadInt16();
        }

        public int ReadInt24 ()
        {
            EnsureAvailable (3);
            int v = m_view.ReadUInt16 (m_start+m_position);
            v |= m_view.ReadByte (m_start+m_position+2) << 16;
            m_position += 3;
            return v;
        }

        public int ReadInt32 ()
        {
            EnsureAvailable (4);
            var v = m_view.ReadInt32 (m_start+m_position);
            m_position += 4;
            return v;
        }

        public uint ReadUInt32 ()
        {
            return (uint)ReadInt32();
        }

        public long ReadInt64 ()
        {
            EnsureAvailable (8);
            var v = m_view.ReadInt64 (m_start+m_position);
            m_position += 8;
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
            uint string_length = (uint)Math.Min (length, m_size-m_position);
            var str = m_view.ReadString (m_position, (uint)string_length, enc);
            m_position += string_length;
            return str;
        }

        public string ReadCString ()
        {
            return ReadCString (Encodings.cp932);
        }

        public string ReadCString (Encoding enc)
        {
            // underlying view includes rest of the stream
            if (m_view.Offset <= m_position && m_view.Offset + m_view.Reserved >= m_start + m_size)
                return ReadCStringUnsafe (enc);

            var string_buf = new byte[0x20];
            int size = 0;
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
            if (0 == count || m_position >= m_size)
                return new byte[0];
            var bytes = m_view.ReadBytes (m_start+m_position, (uint)Math.Min (count, m_size - m_position));
            m_position += bytes.Length;
            return bytes;
        }

        #region System.IO.Stream methods
        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            switch (origin)
            {
            case SeekOrigin.Current:    offset += m_position; break;
            case SeekOrigin.End:        offset += m_size; break;
            }
            Position = offset;
            return m_position;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("GameRes.ArcStream.SetLength method is not supported");
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (0 == count || m_position >= m_size)
                return 0;
            count = (int)Math.Min (count, m_size - m_position);
            int read = m_view.Read (m_start + m_position, buffer, offset, (uint)count);
            m_position += read;
            return read;
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
