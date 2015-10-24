//! \file       ArcBSA.cs
//! \date       Sat Oct 24 15:58:06 2015
//! \brief      Bishop BSA archives.
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Bishop
{
    [Export(typeof(ArchiveFormat))]
    public class BsaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BSA"; } }
        public override string Description { get { return "Bishop resource archive"; } }
        public override uint     Signature { get { return 0x72415342; } } // 'BSAr'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if ('c' != file.View.ReadInt16 (4))
                return null;
            int version = file.View.ReadInt16 (8);
            if (version < 1 || version > 3)
                return null;
            int count = file.View.ReadInt16 (0xA);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xC);
            if (index_offset >= file.MaxOffset)
                return null;
            var reader = new IndexReader (file);
            List<Entry> dir = null;
            if (version > 1)
                dir = reader.ReadV2 (count, index_offset);
            if (null == dir)
                dir = reader.ReadV1 (count, index_offset);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    internal sealed class IndexReader
    {
        ArcView         m_file;
        List<Entry>     m_dir;
        List<string>    m_path;

        public IndexReader (ArcView file)
        {
            m_file = file;
            m_dir = new List<Entry>();
            m_path = new List<string>();
        }

        public List<Entry> ReadV1 (int count, uint index_offset)
        {
            m_file.View.Reserve (index_offset, (uint)(m_file.MaxOffset - index_offset));
            m_dir.Capacity = count;
            m_dir.Clear();
            m_path.Clear();
            for (int i = 0; i < count; ++i)
            {
                string name = m_file.View.ReadString (index_offset, 0x20);
                if (0 == name.Length)
                    return null;
                if ('>' == name[0])
                {
                    m_path.Add (name.Substring (1));
                }
                else if ('<' == name[0])
                {
                    if (m_path.Count > 0)
                        m_path.RemoveAt (m_path.Count-1);
                }
                else
                {
                    if (m_path.Count > 0)
                        name = Path.Combine (GetPathName (name).ToArray());
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = m_file.View.ReadUInt32 (index_offset+0x20);
                    entry.Size   = m_file.View.ReadUInt32 (index_offset+0x24);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return null;
                    m_dir.Add (entry);
                }
                index_offset += 0x28;
            }
            return m_dir;
        }

        public List<Entry> ReadV2 (int count, uint index_offset)
        {
            m_file.View.Reserve (index_offset, (uint)(m_file.MaxOffset - index_offset));
            m_dir.Capacity = count;
            m_dir.Clear();
            m_path.Clear();
            uint filenames_offset = index_offset + (uint)(count * 12);
            byte[] names_buf = new byte[m_file.MaxOffset - filenames_offset];
            m_file.View.Read (filenames_offset, names_buf, 0, (uint)names_buf.Length);

            for (int i = 0; i < count; ++i)
            {
                int name_offset = m_file.View.ReadInt32 (index_offset);
                if (name_offset >= names_buf.Length)
                    return null;
                string name = Binary.GetCString (names_buf, name_offset, names_buf.Length-name_offset);
                if (0 == name.Length)
                    return null;
                if ('>' == name[0])
                {
                    m_path.Add (name.Substring (1));
                }
                else if ('<' == name[0])
                {
                    if (m_path.Count > 0)
                        m_path.RemoveAt (m_path.Count-1);
                }
                else
                {
                    if (m_path.Count > 0)
                        name = Path.Combine (GetPathName (name).ToArray());
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = m_file.View.ReadUInt32 (index_offset+4);
                    entry.Size   = m_file.View.ReadUInt32 (index_offset+8);
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return null;
                    m_dir.Add (entry);
                }
                index_offset += 0xC;
            }
            return m_dir;
        }

        IEnumerable<string> GetPathName (string name)
        {
            foreach (var dir in m_path)
                yield return dir;
            yield return name;
        }
    }
}
