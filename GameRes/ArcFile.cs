//! \file       ArcFile.cs
//! \date       Tue Jul 08 12:53:45 2014
//! \brief      Game Archive file class.
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

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace GameRes
{
    public class ArcFile : IDisposable
    {
        private ArcView         m_arc;
        private ArchiveFormat   m_interface;
        private ICollection<Entry> m_dir;

        /// <summary>Tag that identifies this archive format.</summary>
        public string Tag { get { return m_interface.Tag; } }

        /// <summary>Short archive format description.</summary>
        public string Description { get { return m_interface.Description; } }

        /// <summary>Memory-mapped view of the archive.</summary>
        public ArcView File { get { return m_arc; } }

        /// <summary>Archive contents.</summary>
        public ICollection<Entry> Dir { get { return m_dir; } }

        public ArcFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
        {
            m_arc = arc;
            m_interface = impl;
            m_dir = dir;
        }

        /// <summary>
        /// Try to open <paramref name="filename"/> as archive.
        /// </summary>
        /// <returns>
        /// ArcFile object if file is opened successfully, null otherwise.
        /// </returns>
        public static ArcFile TryOpen (string filename)
        {
            var ext = new Lazy<string> (() => Path.GetExtension (filename).TrimStart ('.').ToLower());
            var file = new ArcView (filename);
            try
            {
                uint signature = file.View.ReadUInt32 (0);
                for (;;)
                {
                    var range = FormatCatalog.Instance.LookupSignature<ArchiveFormat> (signature);
                    // check formats that match filename extension first
                    if (range.Skip(1).Any()) // if range.Count() > 1
                        range = range.OrderByDescending (f => f.Extensions.First() == ext.Value);
                    foreach (var impl in range)
                    {
                        try
                        {
                            var arc = impl.TryOpen (file);
                            if (null != arc)
                            {
                                file = null; // file ownership passed to ArcFile
                                return arc;
                            }
                        }
                        catch (Exception X)
                        {
                            // ignore failed open attmepts
                            Trace.WriteLine (string.Format ("[{0}] {1}: {2}", impl.Tag, filename, X.Message));
                            FormatCatalog.Instance.LastError = X;
                        }
                    }
                    if (0 == signature)
                        break;
                    signature = 0;
                }
            }
            finally
            {
                if (null != file)
                    file.Dispose();
            }
            return null;
        }

        /// <summary>
        /// Extract all entries from the archive into current directory.
        /// <paramref name="callback"/> could be used to observe/control extraction process.
        /// </summary>
        public void ExtractFiles (EntryCallback callback)
        {
            int i = 0;
            foreach (var entry in Dir.OrderBy (e => e.Offset))
            {
                var action = callback (i, entry, null);
                if (ArchiveOperation.Abort == action)
                    break;
                if (ArchiveOperation.Skip != action)
                    Extract (entry);
                ++i;
            }
        }

        /// <summary>
        /// Extract specified <paramref name="entry"/> into current directory.
        /// </summary>
        public void Extract (Entry entry)
        {
            if (-1 != entry.Offset)
                m_interface.Extract (this, entry);
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as Stream.
        /// </summary>
        public Stream OpenEntry (Entry entry)
        {
            return m_interface.OpenEntry (this, entry);
        }

        /// <summary>
        /// Create file corresponding to <paramref name="entry"/> within current directory and open
        /// it for writing.
        /// </summary>
        public Stream CreateFile (Entry entry)
        {
            return m_interface.CreateFile (entry);
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
                    m_arc.Dispose();
                m_arc = null;
                disposed = true;
            }
        }
        #endregion
    }

    public class AppendStream : System.IO.Stream
    {
        private Stream      m_base;
        private long        m_start_pos;

        public override bool CanRead  { get { return true; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override long Length   { get { return m_base.Length - m_start_pos; } }
        public override long Position
        {
            get { return m_base.Position - m_start_pos; }
            set { m_base.Position = Math.Max (m_start_pos+value, m_start_pos); }
        }

        public AppendStream (System.IO.Stream file)
        {
            m_base = file;
            m_start_pos = m_base.Seek (0, SeekOrigin.End);
        }

        public AppendStream (System.IO.Stream file, long offset)
        {
            m_base = file;
            m_start_pos = m_base.Seek (offset, SeekOrigin.Begin);
        }

        public Stream BaseStream { get { return m_base; } }

        public override void Flush()
        {
            m_base.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
            {
                offset = Math.Max (offset + m_start_pos, m_start_pos);
            }
            long position = m_base.Seek (offset, origin);
            if (position < m_start_pos)
            {
                m_base.Seek (m_start_pos, SeekOrigin.Begin);
                position = m_start_pos;
            }
            return position - m_start_pos;
        }

        public override void SetLength (long length)
        {
            if (length < 0)
                length = 0;
            m_base.SetLength (length + m_start_pos);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_base.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return m_base.ReadByte();
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            m_base.Write (buffer, offset, count);
        }

        public override void WriteByte (byte value)
        {
            m_base.WriteByte (value);
        }

        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                m_base = null;
                disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}
