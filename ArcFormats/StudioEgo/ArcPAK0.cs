//! \file       ArcPAK0.cs
//! \date       Sun Nov 22 10:31:48 2015
//! \brief      Studio e.go! resource archives.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.Ego
{
    internal struct DirEntry
    {
        public string   Name;
        public int      Parent;
        public int      LastIndex;
    }

    [Export(typeof(ArchiveFormat))]
    public class Pak0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK0/EGO"; } }
        public override string Description { get { return "Studio e.go! resource archive"; } }
        public override uint     Signature { get { return 0x304B4150; } } // 'PAK0'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public Pak0Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset   = file.View.ReadUInt32 (4);
            int  dir_count     = file.View.ReadInt32 (8);
            int  count         = file.View.ReadInt32 (0xC);
            if (data_offset <= 0x14 || data_offset >= file.MaxOffset)
                return null;
            if (!IsSaneCount (dir_count) || !IsSaneCount (count))
                return null;
            var reader = new Pak0Reader (file, data_offset, dir_count, count);
            var dir = reader.ReadIndex();
            return null != dir ? new ArcFile (file, this, dir) : null;
        }
    }

    internal sealed class Pak0Reader
    {
        ArcView     m_file;
        uint        m_data_offset;
        int         m_dir_count;
        int         m_count;
        uint        m_name_pos;

        public Pak0Reader (ArcView file, uint data_offset, int dir_count, int file_count)
        {
            m_file = file;
            m_data_offset   = data_offset;
            m_dir_count     = dir_count;
            m_count         = file_count;
        }

        public List<Entry> ReadIndex ()
        {
            if (m_data_offset > m_file.View.Reserve (0, m_data_offset))
                return null;
            uint index_offset = 0x10;
            m_name_pos = index_offset + (uint)m_dir_count * 8u + (uint)m_count * 0x10u;
            var dirs = new DirEntry[m_dir_count];
            for (int i = 0; i < m_dir_count; ++i)
            {
                dirs[i].Parent    = m_file.View.ReadInt32 (index_offset);
                dirs[i].LastIndex = m_file.View.ReadInt32 (index_offset+4);
                if (dirs[i].Parent >= m_dir_count || dirs[i].Parent == i)
                    return null;
                if (-1 != dirs[i].Parent)
                    dirs[i].Name = ReadNextName();
                index_offset += 8;
            }
            var files = new List<Entry> (m_count);
            int current = 0;
            for (int i = 0; i < m_dir_count; ++i)
            {
                string parent_dir = GetPath (dirs, i);
                while (current < dirs[i].LastIndex)
                {
                    string name = Path.Combine (parent_dir, ReadNextName());
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = m_file.View.ReadUInt32 (index_offset);
                    entry.Size   = m_file.View.ReadUInt32 (index_offset+4);
                    files.Add (entry);
                    index_offset += 0x10;
                    ++current;
                }
            }
            return files;
        }

        string GetPath (IList<DirEntry> dirs, int dir_index)
        {
            List<string> path = new List<string> (2);
            for (int i = dir_index; dirs[i].Parent != -1; i = dirs[i].Parent)
                path.Add (dirs[i].Name);
            if (0 == path.Count)
                return string.Empty;
            path.Reverse();
            return Path.Combine (path.ToArray());
        }

        string ReadNextName ()
        {
            uint length = m_file.View.ReadByte (m_name_pos++);
            var name = m_file.View.ReadString (m_name_pos, length);
            m_name_pos += length;
            return name;
        }
    }
}
