//! \file       ArcAi5Win.cs
//! \date       Mon Jun 29 04:41:29 2015
//! \brief      Ai5Win engine resource archive.
//
// Copyright (C) 2015-2018 by morkt
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
        public override bool      CanWrite { get { return false; } }

        static Ai5Scheme DefaultScheme = new Ai5Scheme { KnownSchemes = new Dictionary<string, ArcIndexScheme>() };
        public Dictionary<string, ArcIndexScheme> KnownSchemes { get { return DefaultScheme.KnownSchemes; } }

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (Ai5Scheme)value; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 == KnownSchemes.Count)
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var reader = new Ai5ArcIndexReader (file, count);
            var dir = reader.TrySchemes (KnownSchemes.Values);
            if (null == dir)
                dir = reader.TrySchemes (reader.GuessSchemes());
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Name.HasAnyOfExtensions ("mes", "lib", "a", "a6", "msk", "x"))
                return new LzssStream (input);
            return input;
        }
    }

    internal class Ai5ArcIndexReader
    {
        protected ArcView       m_file;
        protected int           m_count;
        protected List<Entry>   m_dir;
        protected byte[]        m_name_buf = new byte[0x100];

        public Ai5ArcIndexReader (ArcView file, int count)
        {
            m_file = file;
            m_count = count;
            m_dir = new List<Entry> (m_count);
        }

        public List<Entry> TrySchemes (IEnumerable<ArcIndexScheme> schemes)
        {
            foreach (var scheme in schemes)
            {
                try
                {
                    var dir = Read (scheme);
                    if (dir != null)
                        return dir;
                }
                catch { /* ignore parse errors */ }
            }
            return null;
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
                string name = DecryptName (scheme);
                if (null == name)
                    return null;
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

        internal string DecryptName (ArcIndexScheme scheme)
        {
            int n;
            for (n = 0; n < m_name_buf.Length; ++n)
            {
                if (n > scheme.NameLength)
                    return null;
                m_name_buf[n] ^= scheme.NameKey;
                if (0 == m_name_buf[n])
                    break;
                if (m_name_buf[n] < 0x20)
                    return null;
            }
            if (n != 0)
                return Encodings.cp932.GetString (m_name_buf, 0, n);
            else
                return null;
        }

        internal IEnumerable<ArcIndexScheme> GuessSchemes ()
        {
            if (m_count < 2)
                yield break;
            foreach (int name_length in NameLengths)
            {
                uint data_offset = (uint)((name_length + 8) * m_count + 4);
                byte name_key = m_file.View.ReadByte (3 + name_length);
                uint first_size   = m_file.View.ReadUInt32 (4 + name_length);
                uint first_offset = m_file.View.ReadUInt32 (8 + name_length);
                uint offset_key = data_offset ^ first_offset;
                uint second_offset = m_file.View.ReadUInt32 ((name_length+8) * 2) ^ offset_key;
                if (second_offset < data_offset || second_offset >= m_file.MaxOffset)
                    continue;
                uint size_key = (second_offset - data_offset) ^ first_size;
                if (0 == offset_key || 0 == size_key)
                    continue;
                yield return new ArcIndexScheme {
                    NameLength = name_length,
                    NameKey = name_key,
                    SizeKey = size_key,
                    OffsetKey = offset_key,
                };
            }
        }

        static readonly int[] NameLengths = { 0x14, 0x1E, 0x20, 0x100 };
    }
}
