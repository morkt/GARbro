//! \file       ArcView.cs
//! \date       Mon Jul 07 10:31:10 2014
//! \brief      Memory mapped view of gameres file.
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
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

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

        public ArcViewStream CreateStream ()
        {
            return new ArcViewStream (this);
        }

        public ArcViewStream CreateStream (long offset)
        {
            var size = this.MaxOffset - offset;
            if (size > uint.MaxValue)
                throw new ArgumentOutOfRangeException ("Too large memory mapped stream");
            return new ArcViewStream (this, offset, (uint)size);
        }

        public ArcViewStream CreateStream (long offset, uint size, string name = null)
        {
            return new ArcViewStream (this, offset, size, name);
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
