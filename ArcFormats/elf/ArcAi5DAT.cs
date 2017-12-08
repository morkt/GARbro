//! \file       ArcAi5DAT.cs
//! \date       2017 Dec 06
//! \brief      Ai5Win engine resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Elf
{
    [Export(typeof(ArchiveFormat))]
    public class DatAI5Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/AI5WIN"; } }
        public override string Description { get { return "AI5WIN engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = file.View.ReadUInt32 (4);
            int count = (int)(file.View.ReadUInt32 (0) ^ key);
            if (!IsSaneCount (count))
                return null;
            byte name_key = file.View.ReadByte (0x23);
            var scheme = new ArcIndexScheme {
                NameLength = 0x14, NameKey = name_key, SizeKey = key, OffsetKey = key
            };
            var reader = new Ai5DatIndexReader (file, count);
            var dir = reader.Read (scheme);
            if (dir != null)
                return new ArcFile (file, this, dir);
            return null;
        }
    }

    internal class Ai5DatIndexReader : Ai5ArcIndexReader
    {
        public Ai5DatIndexReader (ArcView file, int count) : base (file, count)
        {
        }

        new public List<Entry> Read (ArcIndexScheme scheme)
        {
            if (scheme.NameLength > m_name_buf.Length)
                m_name_buf = new byte[scheme.NameLength];
            m_dir.Clear();
            int  index_offset = 8;
            uint index_size = (uint)(m_count * (scheme.NameLength + 8));
            if (index_size > m_file.View.Reserve (index_offset, index_size))
                return null;
            for (int i = 0; i < m_count; ++i)
            {
                uint size   = m_file.View.ReadUInt32 (index_offset)   ^ scheme.SizeKey;
                uint offset = m_file.View.ReadUInt32 (index_offset+4) ^ scheme.OffsetKey;
                if (offset < index_size+8)
                    return null;
                index_offset += 8;
                m_file.View.Read (index_offset, m_name_buf, 0, (uint)scheme.NameLength);
                string name = DecryptName (scheme);
                if (null == name)
                    return null;
                index_offset += scheme.NameLength;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (m_file.MaxOffset))
                    return null;
                m_dir.Add (entry);
            }
            return m_dir;
        }
    }
}
