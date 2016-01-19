//! \file       ArcAi5Win.cs
//! \date       Mon Jun 29 04:41:29 2015
//! \brief      Ai5Win engine resource archive.
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

namespace GameRes.Formats.Elf
{
    [Serializable]
    public class ArcIndexScheme
    {
        public int  NameLength;
        public byte NameKey;
        public uint SizeKey;
        public uint OffsetKey;
    }

    [Serializable]
    public class Ai5Scheme : ResourceScheme
    {
        public Dictionary<string, ArcIndexScheme> KnownSchemes;
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcAI5Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AI5WIN"; } }
        public override string Description { get { return "AI5WIN engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public static Dictionary<string, ArcIndexScheme> KnownSchemes = new Dictionary<string, ArcIndexScheme>();

        public ArcAI5Opener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ResourceScheme Scheme
        {
            get { return new Ai5Scheme { KnownSchemes = KnownSchemes }; }
            set { KnownSchemes = ((Ai5Scheme)value).KnownSchemes; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 == KnownSchemes.Count)
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var reader = new IndexReader (file, count);
            foreach (var scheme in KnownSchemes.Values)
            {
                try
                {
                    var dir = reader.Read (scheme);
                    if (dir != null)
                        return new ArcFile (file, this, dir);
                }
                catch { /* ignore parse errors */ }
            }
            return null;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Name.EndsWith (".mes", StringComparison.InvariantCultureIgnoreCase)
                || entry.Name.EndsWith (".lib", StringComparison.InvariantCultureIgnoreCase))
                return new LzssStream (input);
            return input;
        }

        internal class IndexReader
        {
            ArcView         m_file;
            int             m_count;
            List<Entry>     m_dir;
            byte[]          m_name_buf = new byte[0x100];

            public IndexReader (ArcView file, int count)
            {
                m_file = file;
                m_count = count;
                m_dir = new List<Entry> (m_count);
            }

            public List<Entry> Read (ArcIndexScheme scheme)
            {
                if (scheme.NameLength > m_name_buf.Length)
                    m_name_buf = new byte[scheme.NameLength];
                m_dir.Clear();
                int  index_offset = 4;
                uint index_size = (uint)(m_count * (scheme.NameLength + 8));
                if (index_size > m_file.View.Reserve (index_offset, index_size))
                    return null;
                for (int i = 0; i < m_count; ++i)
                {
                    m_file.View.Read (index_offset, m_name_buf, 0, (uint)scheme.NameLength);
                    int n;
                    for (n = 0; n < m_name_buf.Length; ++n)
                    {
                        m_name_buf[n] ^= scheme.NameKey;
                        if (0 == m_name_buf[n])
                            break;
                        if (m_name_buf[n] < 0x20)
                            return null;
                    }
                    if (0 == n)
                        return null;
                    string name = Encodings.cp932.GetString (m_name_buf, 0, n);
                    index_offset += scheme.NameLength;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size   = m_file.View.ReadUInt32 (index_offset)   ^ scheme.SizeKey;
                    entry.Offset = m_file.View.ReadUInt32 (index_offset+4) ^ scheme.OffsetKey;
                    if (entry.Offset < index_size+4 || !entry.CheckPlacement (m_file.MaxOffset))
                        return null;
                    m_dir.Add (entry);
                    index_offset += 8;
                }
                return m_dir;
            }
        }

        /*
        internal class IndexReader
        {
            ArcView     m_file;
            int         m_count;
            byte[]      m_first = new byte[0x108];

            public const int MinNameLength = 0x10;
            public const int MaxNameLength = 0x100;

            public IndexReader (ArcView file)
            {
                m_file = file;
                m_count = m_file.View.ReadInt32 (0);
                m_file.View.Read (4, m_first, 0, m_first.Length);
            }

            ArcIndexScheme  m_scheme = new ArcIndexScheme();

            public ArcIndexScheme Parse ()
            {
                if (m_count <= 0 || m_count > 0xfffff)
                    return null;

                uint supposed_first_offset = (uint)(m_count * (name_length + 8));

                uint first_size = LittleEndian.ToUInt32 (first_entry, name_length);
                uint first_offset = LittleEndian.ToUInt32 (first_entry, name_length+4);

                uint supposed_offset_key = first_offset ^ supposed_first_offset;
                int last_index_offset = 4 + (m_count - 1) * (name_length + 8);
                uint last_size   = m_file.View.ReadUInt32 (last_index_offset + name_length);
                uint last_offset = m_file.View.ReadUInt32 (last_index_offset + name_length + 4);
                last_offset ^= supposed_offset_key;
            }

            bool ParseFirstEntry (int name_length)
            {
                int index_offset = 4;
            }

            public byte NameKey { get; private set; }

            int GuessNameLength (int initial)
            {
                int name_pos = initial;
                byte sym;
                do
                {
                    do
                    {
                        sym = first_entry[name_pos++];
                    }
                    while (name_pos < MaxNameLength && sym != first_entry[name_pos]);
                    if (MaxNameLength == name_pos)
                        return 0;
                    while (name_pos < MaxNameLength && sym == first_entry[name_pos])
                    {
                        ++name_pos;
                    }
                    if (MaxNameLength == name_pos && sym == first_entry[name_pos] && sym == first_entry[name_pos+1])
                        return 0;
                }
                while (name_pos < MinNameLength || 0 != (name_pos & 1));
                NameKey = sym;
                return name_pos;
            }
        }
        */
    }
}
