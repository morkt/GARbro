//! \file       ArcView.cs
//! \date       Mon Jul 07 10:31:10 2014
//! \brief      Memory mapped view of gameres file.
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
                    byte[] new_buffer = new byte[checked(size/2*3)];
                    Array.Copy (buffer, new_buffer, size);
                    buffer = new_buffer;
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
            var num = view.PointerOffset % info.dwAllocationGranularity;
            byte* ptr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer (ref ptr);
            ptr += num;
            return ptr;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void GetSystemInfo (ref SYSTEM_INFO lpSystemInfo);

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

        public ArcView (string name)
        {
            using (var fs = new FileStream(name, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                MaxOffset = fs.Length;
                m_map = MemoryMappedFile.CreateFromFile (fs, null, 0,
                    MemoryMappedFileAccess.Read, null, HandleInheritability.None, true);
                try {
                    View = new Frame (this);
                } catch {
                    m_map.Dispose(); // dispose on error only
                    throw;
                }
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

        public ArcStream CreateStream (long offset, uint size)
        {
            return new ArcStream (this, offset, size);
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
                    m_view.Dispose();
                    m_view = m_arc.CreateViewAccessor (offset, size);
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
                    byte* ptr = m_view.GetPointer (m_offset);
                    try {
                        for (int i = 0; i < data.Length; ++i)
                        {
                            if (ptr[offset-m_offset+i] != data[i])
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

                unsafe
                {
                    byte* ptr = m_view.GetPointer (m_offset);
                    try {
                        Marshal.Copy ((IntPtr)(ptr+(offset-m_offset)), buf, buf_offset, total);
                    } finally {
                        m_view.SafeMemoryMappedViewHandle.ReleasePointer();
                    }
                }
                return total;
            }

            public byte ReadByte (long offset)
            {
                Reserve (offset, 1);
                return m_view.ReadByte (offset-m_offset);
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
                Reserve (offset, size);
                return m_view.ReadString (offset-m_offset, size, enc);
            }

            public string ReadString (long offset, uint size)
            {
                return ReadString (offset, size, Encodings.cp932);
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

        public class ArcStream : System.IO.Stream
        {
            private Frame       m_view;
            private long        m_start;
            private uint        m_size;
            private long        m_position;

            public override bool CanRead  { get { return true; } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return m_size; } }
            public override long Position
            {
                get { return m_position; }
                set { m_position = Math.Max (value, 0); }
            }

            public ArcStream (ArcView file)
            {
                m_view = file.CreateFrame();
                m_start = 0;
                m_size = (uint)Math.Min (file.MaxOffset, uint.MaxValue);
                m_position = 0;
            }

            public ArcStream (ArcView file, long offset, uint size)
            {
                m_view = new Frame (file, offset, size);
                m_start = m_view.Offset;
                m_size = m_view.Reserved;
                m_position = 0;
            }

            /// <summary>
            /// Read stream signature (first 4 bytes) without altering current read position.
            /// </summary>
            public uint ReadSignature ()
            {
                return m_view.ReadUInt32 (m_start);
            }

            #region System.IO.Stream methods
            public override void Flush()
            {
            }

            public override long Seek (long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                case SeekOrigin.Begin:      m_position = offset; break;
                case SeekOrigin.Current:    m_position += offset; break;
                case SeekOrigin.End:        m_position = m_size + offset; break;
                }
                if (m_position < 0)
                    m_position = 0;
                return m_position;
            }

            public override void SetLength (long length)
            {
                throw new NotSupportedException ("GameRes.ArcStream.SetLength method is not supported");
            }

            public override int Read (byte[] buffer, int offset, int count)
            {
                if (m_position >= m_size)
                    return 0;
                count = (int)Math.Min (count, m_size - m_position);
                int read = m_view.Read (m_start + m_position, buffer, offset, (uint)count);
                m_position += read;
                return read;
            }

            public override int ReadByte ()
            {
                if (m_position >= m_size)
                    return -1;
                byte b = m_view.ReadByte (m_start+m_position);
                ++m_position;
                return b;
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
}
