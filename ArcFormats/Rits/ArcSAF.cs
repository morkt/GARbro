//! \file       ArcSAF.cs
//! \date       Mon Jun 01 03:09:22 2015
//! \brief      SAF archive file format implemenation.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Rits
{
    internal class SafArchive : ArcFile
    {
        public readonly int Version;

        public bool LzssCompression { get { return (Version & 2) != 0; } }

        public SafArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SAF"; } }
        public override string Description { get { return "Rit's resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        const byte DefaultKey5 = 0xDF;
        const byte DefaultKey6 = 0xEF;

        public override ArcFile TryOpen (ArcView file)
        {
            int id = file.View.ReadInt16 (0);
            int count = file.View.ReadInt16 (2);
            if (!IsSaneCount (count))
                return null;
            IIndexReader reader;
            if ((id & 0xFF00) == 0x500)
            {
                var index_buffer = new byte[32 * count];
                if (index_buffer.Length != file.View.Read (4, index_buffer, 0, (uint)index_buffer.Length))
                    return null;
                if (0x501 == id)
                    DecryptIndex (index_buffer, count, DefaultKey5);
                reader = new SafIndexReader5 (index_buffer, count);
            }
            else if ((id & 0xFF00) == 0x600)
            {
                int names_length = file.View.ReadInt32 (4);
                if (names_length <= 0 || names_length >= file.MaxOffset)
                    return null;
                uint index_size = (uint)count * 16;
                var index_buffer = file.View.ReadBytes (8, index_size);
                var names_buffer = file.View.ReadBytes (8 + index_size, (uint)names_length);
                if ((id & 1) != 0)
                {
                    DecryptIndexV6 (index_buffer, count, DefaultKey6);
                    DecryptNames (names_buffer, names_length);
                }
                reader = new SafIndexReader6 (index_buffer, names_buffer, count);
            }
            else
                return null;
            var dir = reader.Scan();
            if (0 == dir.Count || dir.Any (e => !e.CheckPlacement (file.MaxOffset)))
                return null;
            return new SafArchive (file, this, dir, id);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry || !packed_entry.IsPacked)
                return input;
            var sarc = arc as SafArchive;
            if (null == sarc || !sarc.LzssCompression)
                return new ZLibStream (input, CompressionMode.Decompress);
            else
                return new LzssStream (input);
        }

        void DecryptIndex (byte[] index, int count, byte start_key)
        {
            int offset = 0;
            for (int i = 0; i < count; ++i)
            {
                byte key = start_key;
                for (int j = 0; j < 0x20; ++j)
                {
                    index[offset++] ^= key++;
                }
            }
        }

        void DecryptIndexV6 (byte[] index, int count, byte start_key)
        {
            int offset = 0;
            for (int i = 0; i < count; ++i)
            {
                byte key = start_key;
                for (int j = 0; j < 0x10; ++j)
                {
                    index[offset++] ^= key++;
                }
            }
        }

        void DecryptNames (byte[] data, int count)
        {
            byte key = 0xFF;
            for (int i = 0; i < count; ++i)
            {
                data[i] ^= key--;
            }
        }
    }

    internal interface IIndexReader
    {
        List<Entry> Scan ();
    }

    internal class SafIndexReader5 : IIndexReader
    {
        protected   byte[]      m_index;
        protected   int         m_count;
        private     List<Entry> m_dir;

        protected   int         EntrySize   = 0x20;
        protected   int         OffsetPos   = 0x14;
        protected   int         SizePos     = 0x18;
        protected   int         UnpackedPos = 0x1C;
        protected   int         DirIndexPos = 0x14;
        protected   int         DirCountPos = 0x1C;

        bool m_ignore_dirs = false;

        public SafIndexReader5 (byte[] index, int count)
        {
            m_index = index;
            m_count = count;
            m_dir = new List<Entry> (count);
        }

        public List<Entry> Scan ()
        {
            string root_name;
            int root_index;
            int root_count;
            if (!IsDir (0))
            {
                root_name = "";
                root_index = 0;
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
                root_index = m_index.ToInt32 (DirIndexPos);
                root_count = m_index.ToInt32 (DirCountPos);
            }
            ReadDir (root_name, root_index, root_count);
            return m_dir;
        }

        void ReadDir (string dir_name, int index, int count)
        {
            if (index + count > m_count)
                throw new InvalidFormatException();
            int index_offset = index * EntrySize;
            for (int i = 0; i < count; ++i, index_offset += EntrySize)
            {
                if (IsDir (index_offset))
                {
                    if (m_ignore_dirs)
                        continue;
                    int subdir_index = m_index.ToInt32 (index_offset + DirIndexPos);
                    if (subdir_index < index + count)
                        continue;
                    var subdir_name = ReadName (index_offset);
                    int subdir_count = m_index.ToInt32 (index_offset + DirCountPos);
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
                        Offset = (long)m_index.ToUInt32 (index_offset+OffsetPos) << 11,
                        Size   = m_index.ToUInt32 (index_offset+SizePos),
                        UnpackedSize = m_index.ToUInt32 (index_offset+UnpackedPos),
                    };
                    entry.IsPacked = entry.UnpackedSize != 0;
                    m_dir.Add (entry);
                }
            }
        }

        protected virtual bool IsDir (int pos)
        {
            return m_index[pos] > 0x7F;
        }

        protected virtual string ReadName (int pos)
        {
            m_index[pos] &= 0x7F;
            string name = Encodings.cp932.GetString (m_index, pos, 0x14);
            return name.TrimEnd();
        }
    }

    internal class SafIndexReader6 : SafIndexReader5
    {
        byte[]  m_names;

        public SafIndexReader6 (byte[] index, byte[] names, int count) : base (index, count)
        {
            m_names = names;

            EntrySize   = 16;
            OffsetPos   = 4;
            SizePos     = 8;
            UnpackedPos = 12;
            DirIndexPos = 4;
            DirCountPos = 12;
        }

        protected override bool IsDir (int pos)
        {
            return m_index[pos+3] > 0x7F;
        }

        protected override string ReadName (int offset)
        {
            int name_pos = m_index.ToInt32 (offset) & 0x7FFFFFFF;
            return Binary.GetCString (m_names, name_pos);
        }
    }
}
