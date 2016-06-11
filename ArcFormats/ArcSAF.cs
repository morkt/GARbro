//! \file       ArcSAF.cs
//! \date       Mon Jun 01 03:09:22 2015
//! \brief      SAF archive file format implemenation.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Lune
{
    [Export(typeof(ArchiveFormat))]
    public class SafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SAF"; } }
        public override string Description { get { return "Lune resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int id = file.View.ReadInt16 (0);
            int count = file.View.ReadInt16 (2);
            if (count <= 0)
                return null;
            var index_buffer = new byte[32 * count];
            if (index_buffer.Length != file.View.Read (4, index_buffer, 0, (uint)index_buffer.Length))
                return null;
            if (0x501 == id)
                DecryptIndex (index_buffer, count);
            var reader = new IndexReader (index_buffer, count);
            var dir = reader.Scan();
            if (0 == dir.Count || dir.Any (e => !e.CheckPlacement (file.MaxOffset)))
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            else
                return new ZLibStream (input, CompressionMode.Decompress);
        }

        void DecryptIndex (byte[] index, int count)
        {
            int offset = 0;
            for (int i = 0; i < count; ++i)
            {
                byte key = 0xdf;
                for (int j = 0; j < 0x20; ++j)
                {
                    index[offset++] ^= key++;
                }
            }
        }

        internal class IndexReader
        {
            byte[]          m_index;
            int             m_count;
            List<Entry>     m_dir;

            public List<Entry> Dir { get { return m_dir; } }

            public IndexReader (byte[] index, int count)
            {
                m_index = index;
                m_count = count;
                m_dir = new List<Entry> (count);
            }

            bool m_ignore_dirs = false;

            public List<Entry> Scan ()
            {
                string root_name;
                int root_offset;
                int root_count;
                if (0 == (m_index[0] & 0x80))
                {
                    root_name = "";
                    root_offset = 0;
                    root_count = m_count;
                    m_ignore_dirs = true;
                }
                else
                {
                    root_name = ReadName (0);
                    if ("root" == root_name)
                    {
                        root_name = "";
                    }
                    root_offset = LittleEndian.ToInt32 (m_index, 0x14);
                    root_count = LittleEndian.ToInt32 (m_index, 0x1c);
                }
                ReadDir (root_name, root_offset, root_count);
                return m_dir;
            }

            void ReadDir (string dir_name, int index, int count)
            {
                if (index + count > m_count)
                    throw new InvalidFormatException();
                int index_offset = index * 0x20;
                for (int i = 0; i < count; ++i, index_offset += 0x20)
                {
                    if (m_index[index_offset] > 0x7f)
                    {
                        if (m_ignore_dirs)
                            continue;
                        int subdir_index = LittleEndian.ToInt32 (m_index, index_offset+0x14);
                        if (subdir_index < index + count)
                            continue;
                        var subdir_name = ReadName (index_offset);
                        int subdir_count = LittleEndian.ToInt32 (m_index, index_offset+0x1c);
                        ReadDir (Path.Combine (dir_name, subdir_name), subdir_index, subdir_count);
                    }
                    else
                    {
                        var name = ReadName (index_offset);
                        name = Path.Combine (dir_name, name);
                        var entry = new PackedEntry
                        {
                            Name = name,
                            Type = FormatCatalog.Instance.GetTypeFromName (name),
                            Offset = 0x800L * LittleEndian.ToUInt32 (m_index, index_offset+0x14),
                            Size   = LittleEndian.ToUInt32 (m_index, index_offset+0x18),
                            UnpackedSize = LittleEndian.ToUInt32 (m_index, index_offset+0x1c),
                        };
                        entry.IsPacked = entry.UnpackedSize != 0;
                        m_dir.Add (entry);
                    }
                }
            }

            string ReadName (int offset)
            {
                m_index[offset] &= 0x7f;
                string name = Encodings.cp932.GetString (m_index, offset, 0x14);
                return name.TrimEnd();
            }
        }
    }
}
