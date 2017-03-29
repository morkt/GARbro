//! \file       ArcView.cs
//! \date       Mon Jul 07 10:31:10 2014
//! \brief      Memory mapped view of gameres file.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using GameRes.Utility;

namespace GameRes
{
    public static class Encodings
    {
        public static readonly Encoding cp932 = Encoding.GetEncoding (932);

        public static Encoding WithFatalFallback (this Encoding enc)
        {
            var encoding = enc.Clone() as Encoding;
            encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
            encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
            return encoding;
        }
    }

    public static class StreamExtension
    {
        static public string ReadStringUntil (this Stream file, byte delim, Encoding enc)
        {
            byte[] buffer = new byte[16];
            int size = 0;
            for (;;)
            {
                int b = file.ReadByte ();
                if (-1 == b || delim == b)
                    break;
                if (buffer.Length == size)
                {
                    Array.Resize (ref buffer, checked(size/2*3));
                }
                buffer[size++] = (byte)b;
            }
            return enc.GetString (buffer, 0, size);
        }

        static public string ReadCString (this Stream file, Encoding enc)
        {
            return ReadStringUntil (file, 0, enc);
        }

        static public string ReadCString (this Stream file)
        {
            return ReadStringUntil (file, 0, Encodings.cp932);
        }
    }

    public static class MappedViewExtension
    {
        static public string ReadString (this MemoryMappedViewAccessor view, long offset, uint size, Encoding enc)
        {
            if (0 == size)
                return string.Empty;
            byte[] buffer = new byte[size];
            uint n;
            for (n = 0; n < size; ++n)
            {
                byte b = view.ReadByte (offset+n);
                if (0 == b)
                    break;
                buffer[n] = b;
            }
            return enc.GetString (buffer, 0, (int)n);
        }

        static public string ReadString (this MemoryMappedViewAccessor view, long offset, uint size)
        {
            return ReadString (view, offset, size, Encodings.cp932);
        }

        unsafe public static byte* GetPointer (this MemoryMappedViewAccessor view, long offset)
        {
            var num = offset % info.dwAllocationGranularity;
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer (ref ptr);
            ptr += num;
            return ptr;
        }

        [DllImport("kernel32.dll", SetLastError = false)]
        internal static extern void GetSystemInfo (ref SYSTEM_INFO lpSystemInfo);

        [StructLayout (LayoutKind.Sequential)]
        internal struct SYSTEM_INFO
        {
            internal int dwOemId;
            internal int dwPageSize;
            internal IntPtr lpMinimumApplicationAddress;
            internal IntPtr lpMaximumApplicationAddress;
            internal IntPtr dwActiveProcessorMask;
            internal int dwNumberOfProcessors;
            internal int dwProcessorType;
            internal int dwAllocationGranularity;
            internal short wProcessorLevel;
            internal short wProcessorRevision;
        }

        static SYSTEM_INFO info;

        static MappedViewExtension()
        {
            GetSystemInfo (ref info);
        }
    }

    public class ArcView : IDisposable
    {
        private MemoryMappedFile    m_map;

        public const long           PageSize = 4096;
        public long                 MaxOffset { get; private set; }
        public Frame                View { get; private set; }
        public string               Name { get; private set; }

        public ArcView (string name)
        {
            using (var fs = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Name = name;
                MaxOffset = fs.Length;
                InitFromFileStream (fs, 0);
            }
        }

        public ArcView (Stream input, string name, uint length)
        {
            Name = name;
            MaxOffset = length;
            if (input is FileStream)
                InitFromFileStream (input as FileStream, length);
            else
                InitFromStream (input, length);
        }

        private void InitFromFileStream (FileStream fs, uint length)
        {
            m_map = MemoryMappedFile.CreateFromFile (fs, null, length,
                MemoryMappedFileAccess.Read, null, HandleInheritability.None, true);
            try {
                View = new Frame (this);
            } catch {
                m_map.Dispose(); // dispose on error only
                throw;
            }
        }

        private void InitFromStream (Stream input, uint length)
        {
            m_map = MemoryMappedFile.CreateNew (null, length, MemoryMappedFileAccess.ReadWrite,
                MemoryMappedFileOptions.None, null, HandleInheritability.None);
            try
            {
                using (var view = m_map.CreateViewAccessor (0, length, MemoryMappedFileAccess.Write))
                {
                    var buffer = new byte[81920];
                    unsafe
                    {
                        byte* ptr = view.GetPointer (0);
                        try
                        {
                            uint total = 0;
                            while (total < length)
                            {
                                int read = input.Read (buffer, 0, buffer.Length);
                                if (0 == read)
                                    break;
                                read = (int)Math.Min (read, length-total);
                                Marshal.Copy (buffer, 0, (IntPtr)(ptr+total), read);
                                total += (uint)read;
                            }
                            MaxOffset = total;
                        }
                        finally
                        {
                            view.SafeMemoryMappedViewHandle.ReleasePointer();
                        }
                    }
                }
                View = new Frame (this);
            } 
            catch
            {
                m_map.Dispose();
                throw;
            }
        }

        public Frame CreateFrame ()
        {
            return new Frame (View);
        }

        public ArcStream CreateStream ()
        {
            return new ArcStream (this);
        }

        public ArcStream CreateStream (long offset)
        {
            var size = this.MaxOffset - offset;
            if (size > uint.MaxValue)
                throw new ArgumentOutOfRangeException ("Too large memory mapped stream");
            return new ArcStream (this, offset, (uint)size);
        }

        public ArcStream CreateStream (long offset, uint size, string name = null)
        {
            return new ArcStream (this, offset, size, name);
        }
        
        public MemoryMappedViewAccessor CreateViewAccessor (long offset, uint size)
        {
            return m_map.CreateViewAccessor (offset, size, MemoryMappedFileAccess.Read);
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    View.Dispose();
                    m_map.Dispose();
                }
                disposed = true;
                m_map = null;
            }
        }
        #endregion

        public class Frame : IDisposable
        {
            private ArcView                     m_arc;
            private MemoryMappedViewAccessor    m_view;
            private long                        m_offset;
            private uint                        m_size;

            public long Offset      { get { return m_offset; } }
            public uint Reserved    { get { return m_size; } }

            public Frame (ArcView arc)
            {
                m_arc = arc;
                m_offset = 0;
                m_size = (uint)Math.Min (ArcView.PageSize, m_arc.MaxOffset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
            }

            public Frame (Frame other)
            {
                m_arc = other.m_arc;
                m_offset = 0;
                m_size = (uint)Math.Min (ArcView.PageSize, m_arc.MaxOffset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
            }

            public Frame (ArcView arc, long offset, uint size)
            {
                m_arc = arc;
                m_offset = Math.Min (offset, m_arc.MaxOffset);
                m_size = (uint)Math.Min (size, m_arc.MaxOffset-m_offset);
                m_view = m_arc.CreateViewAccessor (m_offset, m_size);
            }

            public uint Reserve (long offset, uint size)
            {
                if (offset < m_offset || offset+size > m_offset+m_size)
                {
                    if (offset > m_arc.MaxOffset)
                        throw new ArgumentOutOfRangeException ("offset", "Too large offset specified for memory mapped file view.");
                    if (size < ArcView.PageSize)
                        size = (uint)ArcView.PageSize;
                    if (size > m_arc.MaxOffset-offset)
                        size = (uint)(m_arc.MaxOffset-offset);
                    var old_view = m_view;
                    m_view = m_arc.CreateViewAccessor (offset, size);
                    old_view.Dispose();
                    m_offset = offset;
                    m_size = size;
                }
                return (uint)(m_offset + m_size - offset);
            }

            public bool AsciiEqual (long offset, string data)
            {
                if (Reserve (offset, (uint)data.Length) < (uint)data.Length)
                    return false;
                unsafe
                {
                    byte* ptr = m_view.GetPointer (m_offset) + (offset - m_offset);
                    try {
                        for (int i = 0; i < data.Length; ++i)
                        {
                            if (ptr[i] != data[i])
                                return false;
                        }
                    } finally {
                        m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                    return true;
                }
            }

            public int Read (long offset, byte[] buf, int buf_offset, uint count)
            {
                // supposedly faster version of
                //Reserve (offset, count);
                //return m_view.ReadArray (offset-m_offset, buf, buf_offset, (int)count);

                if (buf == null)
                    throw new ArgumentNullException ("buf", "Buffer cannot be null.");
                if (buf_offset < 0)
                    throw new ArgumentOutOfRangeException ("buf_offset", "Buffer offset should be non-negative.");

                int total = (int)Math.Min (Reserve (offset, count), count);
                if (buf.Length - buf_offset < total)
                    throw new ArgumentException ("Buffer offset and length are out of bounds.");
                UnsafeCopy (offset, buf, buf_offset, total);
                return total;
            }

            private unsafe void UnsafeCopy (long offset, byte[] buf, int buf_offset, int count)
            {
                byte* ptr = m_view.GetPointer (m_offset);
                try {
                    Marshal.Copy ((IntPtr)(ptr+(offset-m_offset)), buf, buf_offset, count);
                } finally {
                    m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
            }

            /// <summary>
            /// Read <paramref name="count"/> bytes starting from <paramref name="offset"/> into byte array and return that array.
            /// Returned array could be less than <paramref name="count"/> bytes length if end of the mapped file was reached.
            /// </summary>
            public byte[] ReadBytes (long offset, uint count)
            {
                count = Math.Min (count, Reserve (offset, count));
                var data = new byte[count];
                if (count != 0)
                    UnsafeCopy (offset, data, 0, data.Length);
                return data;
            }

            public byte ReadByte (long offset)
            {
                Reserve (offset, 1);
                return m_view.ReadByte (offset-m_offset);
            }

            public sbyte ReadSByte (long offset)
            {
                Reserve (offset, 1);
                return m_view.ReadSByte (offset-m_offset);
            }

            public ushort ReadUInt16 (long offset)
            {
                Reserve (offset, 2);
                return m_view.ReadUInt16 (offset-m_offset);
            }

            public short ReadInt16 (long offset)
            {
                Reserve (offset, 2);
                return m_view.ReadInt16 (offset-m_offset);
            }

            public uint ReadUInt32 (long offset)
            {
                Reserve (offset, 4);
                return m_view.ReadUInt32 (offset-m_offset);
            }

            public int ReadInt32 (long offset)
            {
                Reserve (offset, 4);
                return m_view.ReadInt32 (offset-m_offset);
            }

            public ulong ReadUInt64 (long offset)
            {
                Reserve (offset, 8);
                return m_view.ReadUInt64 (offset-m_offset);
            }

            public long ReadInt64 (long offset)
            {
                Reserve (offset, 8);
                return m_view.ReadInt64 (offset-m_offset);
            }

            public string ReadString (long offset, uint size, Encoding enc)
            {
                size = Math.Min (size, Reserve (offset, size));
                return m_view.ReadString (offset-m_offset, size, enc);
                /* unsafe implementation requires .Net v4.6                
                if (0 == size)
                    return string.Empty;
                unsafe
                {
                    byte* s = m_view.GetPointer (m_offset) + (offset - m_offset);
                    try
                    {
                        uint string_length = 0;
                        while (string_length < size && 0 != s[string_length])
                        {
                            ++string_length;
                        }
                        return enc.GetString (s, (int)string_length); // .Net v4.6+ only
                    }
                    finally
                    {
                        m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                }
                */
            }

            public string ReadString (long offset, uint size)
            {
                return ReadString (offset, size, Encodings.cp932);
            }

            public unsafe ViewPointer GetPointer ()
            {
                return new ViewPointer (m_view, m_offset);
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                    {
                        m_view.Dispose();
                    }
                    m_arc = null;
                    m_view = null;
                    disposed = true;
                }
            }
            #endregion
        }

        public class ArcStream : Stream, IBinaryStream
        {
            private readonly Frame  m_view;
            private readonly long   m_start;
            private readonly long   m_size;
            private long        m_position;
            private byte[]      m_buffer;
            private int         m_buffer_pos;   // read position within buffer
            private int         m_buffer_len;   // length of bytes read in buffer

            private const int DefaultBufferSize = 0x1000;

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

            public ArcStream (ArcView file)
            {
                m_view = file.CreateFrame();
                m_start = 0;
                m_size = file.MaxOffset;
                m_position = 0;
                Name = file.Name;
            }

            public ArcStream (Frame view, string name = null)
            {
                m_view = view;
                m_start = m_view.Offset;
                m_size = m_view.Reserved;
                m_position = 0;
                Name = name ?? "";
            }

            public ArcStream (ArcView file, long offset, uint size, string name = null)
                : this (new Frame (file, offset, size), name)
            {
            }

            public ArcStream (Frame view, long offset, uint size, string name = null)
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
                    else if (null == m_buffer)
                        m_buffer = new byte[DefaultBufferSize];
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
//                    return enc.GetString (s, string_length); // .Net v4.6+ only
                    var string_buf = new byte[string_length];
                    Marshal.Copy ((IntPtr)s, string_buf, 0, string_length);
                    return enc.GetString (string_buf, 0, string_length);
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
                    return new byte[0];
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
                case SeekOrigin.Begin:      Position = offset; break;
                case SeekOrigin.Current:    Position += offset; break;
                case SeekOrigin.End:        Position = m_size + offset; break;
                }
                return Position;
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

        public class Reader : System.IO.BinaryReader
        {
            public Reader (Stream stream) : base (stream, Encoding.ASCII, true)
            {
            }
        }
    }

    /// <summary>
    /// Unsafe wrapper around unmanaged memory mapped view pointer.
    /// </summary>
    public unsafe class ViewPointer : IDisposable
    {
        MemoryMappedViewAccessor    m_view;
        byte*                       m_ptr;

        public ViewPointer (MemoryMappedViewAccessor view, long offset)
        {
            m_view = view;
            m_ptr = m_view.GetPointer (offset);
        }

        public byte* Value
        {
            get
            {
                if (!_disposed)
                    return m_ptr;
                else
                    throw new ObjectDisposedException ("Access to disposed ViewPointer object failed.");
            }
        }

        #region IDisposable Members
        bool _disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                }
                _disposed = true;
            }
        }
        #endregion
    }
}
