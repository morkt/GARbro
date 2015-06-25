//! \file       ArcLIB.cs
//! \date       Thu Jun 25 06:46:51 2015
//! \brief      Malie System archive implementation.
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

namespace GameRes.Formats.Malie
{
    [Export(typeof(ArchiveFormat))]
    public class LibOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LIB"; } }
        public override string Description { get { return "Malie engine resource archive"; } }
        public override uint     Signature { get { return 0x0042494C; } } // 'LIB'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var reader = new Reader (file);
            if (reader.ReadIndex ("", 0, (uint)file.MaxOffset))
                return new ArcFile (file, this, reader.Dir);
            else
                return null;
        }

        internal class Reader
        {
            ArcView.Frame   m_view;
            List<Entry>     m_dir = new List<Entry>();

            public List<Entry> Dir { get { return m_dir; } }

            public Reader (ArcView file)
            {
                m_view = file.View;
            }

            public bool ReadIndex (string root, long base_offset, uint size)
            {
                uint signature = m_view.ReadUInt32 (base_offset);
                if (0x0042494C != signature)
                    return false;

                int count = m_view.ReadInt16 (base_offset + 8);
                if (count <= 0)
                    return false;
                long index_offset = base_offset + 0x10;
                uint index_size = (uint)(0x30 * count);
                if (index_size > size)
                    return false;
                if (index_size > m_view.Reserve (index_offset, index_size))
                    return false;
                long data_offset = index_offset + index_size;
                if (m_dir.Capacity < m_dir.Count + count)
                    m_dir.Capacity = m_dir.Count + count;
                for (int i = 0; i < count; ++i)
                {
                    string name = m_view.ReadString (index_offset, 0x24);
                    uint entry_size = m_view.ReadUInt32 (index_offset+0x24);
                    long offset = base_offset + m_view.ReadUInt32 (index_offset+0x28);
                    index_offset += 0x30;
                    string ext = Path.GetExtension (name);
                    name = Path.Combine (root, name);
                    if (string.IsNullOrEmpty (ext) && ReadIndex (name, offset, entry_size))
                    {
                        continue;
                    }
                    if (offset < data_offset || offset + entry_size > base_offset + size)
                        return false;

                    var entry = FormatCatalog.Instance.CreateEntry (name);
                    entry.Offset = offset;
                    entry.Size   = entry_size;
                    m_dir.Add (entry);
                }
                return true;
            }
        }
    }
}
