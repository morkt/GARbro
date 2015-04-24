//! \file       ArcNOA.cs
//! \date       Thu Apr 23 15:57:17 2015
//! \brief      Entis GLS engine archives implementation.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Entis
{
    internal class NoaEntry : Entry
    {
        public byte[]   Extra;
        public uint     Encryption;
        public uint     Attr;
    }

    [Export(typeof(ArchiveFormat))]
    public class NoaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NOA"; } }
        public override string Description { get { return "Entis GLS engine resource archive"; } }
        public override uint     Signature { get { return 0x69746e45; } } // 'Enti'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "Entis\x1a"))
                return null;
            uint id = file.View.ReadUInt32 (8);
            if (0x02000400 != id)
                return null;
            var reader = new IndexReader (file);
            if (!reader.ParseDirEntry (0x40, ""))
                return null;
            return new ArcFile (file, this, reader.Dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nent = entry as NoaEntry;
            if (null == nent || !arc.File.View.AsciiEqual (entry.Offset, "filedata"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            ulong size = arc.File.View.ReadUInt64 (entry.Offset+8);
            if (size > uint.MaxValue)
                throw new FileSizeException();
            if (0 == nent.Encryption)
                return arc.File.CreateStream (entry.Offset+0x10, (uint)size);
            if (0x40000000 != nent.Encryption)
            {
                System.Diagnostics.Trace.WriteLine (string.Format ("{0}: unknown encryption scheme 0x{1:x8}",
                    nent.Name, nent.Encryption));
                return arc.File.CreateStream (entry.Offset+0x10, (uint)size);
            }
            return arc.File.CreateStream (entry.Offset+0x10, (uint)size);
        }

        internal class IndexReader
        {
            ArcView     m_file;
            List<Entry> m_dir = new List<Entry>();

            const char PathSeparatorChar = '/';

            public List<Entry> Dir { get { return m_dir; } }

            public IndexReader (ArcView file)
            {
                m_file = file;
            }

            public bool ParseDirEntry (long dir_offset, string cur_dir)
            {
                if (!m_file.View.AsciiEqual (dir_offset, "DirEntry"))
                    return false;
                long size = m_file.View.ReadInt64 (dir_offset+8);
                if (size <= 0 || size > int.MaxValue)
                    return false;
                if ((uint)size > m_file.View.Reserve (dir_offset+8, (uint)size))
                    return false;
                long base_offset = dir_offset;
                dir_offset += 0x10;
                int count = m_file.View.ReadInt32 (dir_offset);
                dir_offset += 4;
                if (m_dir.Capacity < m_dir.Count+count)
                    m_dir.Capacity = m_dir.Count+count;
                for (int i = 0; i < count; ++i)
                {
                    var entry = new NoaEntry();
                    entry.Size = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 8;
                    entry.Attr = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;
                    entry.Encryption = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;
                    entry.Offset = base_offset + m_file.View.ReadInt64 (dir_offset);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    dir_offset += 0x10;
                    uint extra_length = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;
                    if (extra_length > 0 && 0 == (entry.Attr & 0x70))
                    {
                        entry.Extra = new byte[extra_length];
                        if (entry.Extra.Length != m_file.View.Read (dir_offset, entry.Extra, 0, extra_length))
                            return false;
                    }
                    dir_offset += extra_length;
                    uint name_length = m_file.View.ReadUInt32 (dir_offset);
                    dir_offset += 4;
                    string name = m_file.View.ReadString (dir_offset, name_length);
                    dir_offset += name_length;
                    if (string.IsNullOrEmpty (cur_dir))
                        entry.Name = name;
                    else
                        entry.Name = cur_dir + PathSeparatorChar + name;
                    if (0x10 == entry.Attr)
                    {
                        if (!ParseDirEntry (entry.Offset+0x10, entry.Name))
                            return false;
                    }
                    else if (0x20 == entry.Attr || 0x40 == entry.Attr)
                    {
                        break;
                    }
                    else
                    {
                        m_dir.Add (entry);
                    }
                }
                return true;
            }
        }
    }
}
