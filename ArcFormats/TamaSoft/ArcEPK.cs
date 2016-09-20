//! \file       ArcEPK.cs
//! \date       Tue Sep 20 00:07:35 2016
//! \brief      TamaSoft resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Tama
{
    internal class EpkEntry : Entry
    {
        public int  ArcNumber;
    }

    internal class EpkArchive : ArcFile
    {
        public readonly IReadOnlyList<ArcView>  Parts;

        public EpkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, IReadOnlyList<ArcView> parts)
            : base (arc, impl, dir)
        {
            Parts = parts;
        }

        bool _epk_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_epk_disposed)
                return;

            if (disposing)
            {
                foreach (var arc in Parts)
                    arc.Dispose();
            }
            _epk_disposed = true;
            base.Dispose (disposing);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "EPK"; } }
        public override string Description { get { return "TamaSoft ADV system resource archive"; } }
        public override uint     Signature { get { return 0x204B5045; } } // 'EPK '
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "EPK "))
                return null;
            uint index_size = file.View.ReadUInt32 (4) - 0x20;
            int count = file.View.ReadInt32 (0x18);
            if (!IsSaneCount (count) || index_size >= file.MaxOffset)
                return null;
            uint index_offset = 0x20;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var arc_list = new List<Entry>();
            var arc_dir = VFS.GetDirectoryName (file.Name);
            var arc_name = Path.GetFileNameWithoutExtension (file.Name);
            for (int i = 1; i < 10; ++i)
            {
                var part_name = string.Format ("{0}.e{1:D02}", arc_name, i);
                part_name = VFS.CombinePath (arc_dir, part_name);
                if (!VFS.FileExists (part_name))
                    break;
                arc_list.Add (VFS.FindFile (part_name));
            }

            var name_buffer = new byte[0x40];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_offset = file.View.ReadUInt32 (index_offset+8);
                int name_length = file.View.ReadInt32 (name_offset);
                if (name_length <= 0 || name_length >= index_size)
                    return null;
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                file.View.Read (name_offset+4, name_buffer, 0, (uint)name_length);
                for (int j = 0; j < name_length; ++j)
                    name_buffer[j] ^= 0xFF;
                var name = Encodings.cp932.GetString (name_buffer, 0, name_length);

                var entry = FormatCatalog.Instance.Create<EpkEntry> (name);
                entry.Offset = file.View.ReadInt64 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                dir.Add (entry);
                index_offset += 0x28;
            }
            var arc_set = new List<ArcView> (arc_list.Count);
            try
            {
                long max_offset = file.MaxOffset;
                var bounds = new List<long> (arc_list.Count+1);
                bounds.Add (max_offset);
                foreach (var arc_entry in arc_list)
                {
                    var arc_file = VFS.OpenView (arc_entry);
                    arc_set.Add (arc_file);
                    max_offset += arc_file.MaxOffset;
                    bounds.Add (max_offset);
                }
                foreach (EpkEntry entry in dir)
                {
                    if (!entry.CheckPlacement (max_offset))
                        return null;
                    entry.ArcNumber = bounds.FindIndex (x => x > entry.Offset);
                    if (entry.ArcNumber > 0)
                        entry.Offset -= bounds[entry.ArcNumber-1];
                }
                var arc = new EpkArchive (file, this, dir, arc_set);
                arc_set = null;
                return arc;
            }
            finally
            {
                if (arc_set != null)
                {
                    foreach (var arc in arc_set)
                        arc.Dispose();
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var earc = arc as EpkArchive;
            var eent = entry as EpkEntry;
            if (null == earc || null == eent)
                return base.OpenEntry (arc, entry);
            long entry_offset = entry.Offset;
            ArcView file = arc.File;
            if (eent.ArcNumber > 0)
                file = earc.Parts[eent.ArcNumber-1];
            if (entry_offset + entry.Size <= file.MaxOffset)
                return file.CreateStream (entry_offset, entry.Size);
            uint first_part_size = (uint)(file.MaxOffset - entry_offset);
            var begin = file.CreateStream (entry_offset, first_part_size);
            var end = earc.Parts[eent.ArcNumber].CreateStream (0, entry.Size - first_part_size);
            return new ConcatStream (begin, end);
        }
    }

    /// <summary>
    /// Concatenation of the two input Streams.
    /// </summary>
    public class ConcatStream : InputProxyStream
    {
        Stream      m_second;
        long        m_position;
        Stream      m_active;

        public ConcatStream (Stream first, Stream second) : base (first)
        {
            m_second = second;
            m_position = 0;
            m_active = first;
        }

        internal Stream  First { get { return BaseStream; } }
        internal Stream Second { get { return m_second; } }

        public override bool CanSeek  { get { return First.CanSeek && Second.CanSeek; } }
        public override long Length   { get { return First.Length + Second.Length; } }
        public override long Position
        {
            get { return m_position; }
            set { m_position = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (First.CanSeek)
            {
                if (m_position >= First.Length)
                {
                    m_active = Second;
                    m_active.Position = m_position - First.Length;
                }
                else
                {
                    m_active = First;
                    m_active.Position = m_position;
                }
            }
            int total_read = 0;
            while (count > 0)
            {
                int read = m_active.Read (buffer, offset, count);
                if (0 == read)
                    break;
                total_read += read;
                m_position += read;
                offset += read;
                count -= read;
            }
            if (count > 0 && m_active != Second)
            {
                m_active = Second;
                if (m_active.CanSeek)
                    m_active.Position = 0;
                int read = m_active.Read (buffer, offset, count);
                m_position += read;
                total_read += read;
            }
            return total_read;
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                    m_second.Dispose();
                _disposed = true;
                base.Dispose (disposing);
            }
        }
    }
}
