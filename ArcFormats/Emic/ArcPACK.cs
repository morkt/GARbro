//! \file       ArcPACK.cs
//! \date       Sun Aug 30 01:11:18 2015
//! \brief      Emic engine archive implementation.
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

namespace GameRes.Formats.Emic
{
    internal class EmicArchive : ArcFile
    {
        public readonly byte[] Key;

        public EmicArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/EMIC"; } }
        public override string Description { get { return "Emic engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x28);
            if (!IsSaneCount (count))
                return null;

            var reader = new PacReader (file, count);
            var dir = reader.ReadIndex();
            if (null == dir)
                return null;
            if (reader.Encrypted)
                return new EmicArchive (file, this, dir, reader.Key);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var emic = arc as EmicArchive;
            if (null == emic)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var reader = new PacReader (emic.File, emic.Key);
            var data = new byte[entry.Size];
            reader.Read (entry.Offset, data, 0, data.Length);
            return new MemoryStream (data);
        }
    }

    internal class PacReader
    {
        ArcView     m_file;
        int         m_count;

        public bool Encrypted { get; private set; }
        public byte[]     Key { get; private set; }

        public PacReader (ArcView file, int count)
        {
            m_file = file;
            m_count = count;
            Key = new byte[0x20];
            Encrypted = 1 == m_file.View.ReadInt32 (4);
            file.View.Read (8, Key, 0, (uint)Key.Length);
            for (int i = 0; i < Key.Length; ++i)
                Key[i] ^= 0xAA;
        }

        public PacReader (ArcView file, byte[] key)
        {
            m_file = file;
            Encrypted = true;
            Key = key;
        }

        public List<Entry> ReadIndex ()
        {
            var index_buf = new byte[0x110];
            var dir = new List<Entry> (m_count);
            uint index_offset = 0x2C;
            for (int i = 0; i < m_count; ++i)
            {
                Read (index_offset, index_buf, 0, 4);
                int name_len = LittleEndian.ToInt32 (index_buf, 0);
                if (name_len > index_buf.Length-8) // file name is too long
                    return null;
                index_offset += 4;
                Read (index_offset, index_buf, 0, name_len+8);
                string name = Binary.GetCString (index_buf, 0, name_len);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index_buf, name_len+4);
                entry.Size   = LittleEndian.ToUInt32 (index_buf, name_len);
                if (!entry.CheckPlacement (m_file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += (uint)name_len + 8;
            }
            return dir;
        }

        public int Read (long offset, byte[] buf, int index, int count)
        {
            int read = m_file.View.Read (offset, buf, index, (uint)count);
            if (Encrypted)
            {
                int key_offset = (int)offset;
                for (int i = 0; i < read; ++i)
                {
                    buf[i] ^= Key[key_offset++ & 0x1F];
                }
            }
            return read;
        }
    }
}
