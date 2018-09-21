//! \file       ArcFile.cs
//! \date       Tue Jul 08 12:53:45 2014
//! \brief      Game Archive file class.
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

        /// <summary>Tags of formats related to this archive format (could be null).</summary>
        public IEnumerable<string> ContainedFormats { get { return m_interface.ContainedFormats; } }

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
            return TryOpen (VFS.FindFile (filename));
        }

        public static ArcFile TryOpen (Entry entry)
        {
            if (entry.Size < 4)
                return null;
            var file = VFS.OpenView (entry);
            try
            {
                uint signature = file.View.ReadUInt32 (0);
                foreach (var impl in FormatCatalog.Instance.FindFormats<ArchiveFormat> (entry.Name, signature))
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
                    catch (OperationCanceledException X)
                    {
                        FormatCatalog.Instance.LastError = X;
                        return null;
                    }
                    catch (Exception X)
                    {
                        // ignore failed open attmepts
                        Trace.WriteLine (string.Format ("[{0}] {1}: {2}", impl.Tag, entry.Name, X.Message));
                        FormatCatalog.Instance.LastError = X;
                    }
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
        /// Open specified <paramref name="entry"/> as memory-mapped view.
        /// </summary>
        public ArcView OpenView (Entry entry)
        {
            using (var stream = OpenEntry (entry))
            {
                uint size;
                var packed_entry = entry as PackedEntry;
                if (stream.CanSeek)
                    size = (uint)stream.Length;
                else if (null != packed_entry && packed_entry.IsPacked)
                {
                    size = packed_entry.UnpackedSize;
                    if (0 == size)
                    {
                        using (var copy = new MemoryStream())
                        {
                            stream.CopyTo (copy);
                            copy.Position = 0;
                            return new ArcView (copy, entry.Name, (uint)copy.Length);
                        }
                    }
                }
                else
                    size = entry.Size;
                if (0 == size)
                    throw new FileSizeException (Strings.garStrings.MsgFileIsEmpty);
                return new ArcView (stream, entry.Name, size);
            }
        }

        /// <summary>
        /// Open specified <paramref name="entry"/> as a seekable Stream.
        /// </summary>
        public Stream OpenSeekableEntry (Entry entry)
        {
            var input = OpenEntry (entry);
            if (input.CanSeek)
                return input;
            using (input)
            {
                int capacity = (int)entry.Size;
                var packed_entry = entry as PackedEntry;
                if (packed_entry != null && packed_entry.UnpackedSize != 0)
                    capacity = (int)packed_entry.UnpackedSize;
                var copy = new MemoryStream (capacity);
                input.CopyTo (copy);
                copy.Position = 0;
                return copy;
            }
        }

        public IBinaryStream OpenBinaryEntry (Entry entry)
        {
            var input = OpenSeekableEntry (entry);
            return BinaryStream.FromStream (input, entry.Name);
        }

        public IImageDecoder OpenImage (Entry entry)
        {
            return m_interface.OpenImage (this, entry);
        }

        public ArchiveFileSystem CreateFileSystem ()
        {
            if (m_interface.IsHierarchic)
                return new TreeArchiveFileSystem (this);
            else
                return new FlatArchiveFileSystem (this);
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
                disposed = true;
            }
        }
        #endregion
    }
}
