//! \file       ArcTactics.cs
//! \date       Thu Jul 23 16:27:55 2015
//! \brief      Tactics archive file implementation.
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
using System.Security.Cryptography;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Tactics
{
    internal class TacticsArcFile : ArcFile
    {
        public byte[] Password;

        public TacticsArcFile (ArcView file, ArchiveFormat format, ICollection<Entry> dir, byte[] pass)
            : base (file, format, dir)
        {
            Password = pass;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Tactics"; } }
        public override string Description { get { return "Tactics archive file"; } }
        public override uint     Signature { get { return 0x54434154; } } // 'TACT'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc", "adf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ICS_ARC_FILE"))
                return null;

            var reader = new IndexReader (file);
            var dir = reader.ReadIndex();
            if (null == dir)
                return null;
            return new TacticsArcFile (file, this, dir, reader.Password);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tarc = arc as TacticsArcFile;
            var tent = entry as PackedEntry;
            if (null == tarc || null == tarc.Password && !tent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == tarc.Password)
                return new LzssStream (arc.File.CreateStream (entry.Offset, entry.Size));

            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int p = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= tarc.Password[p++];
                if (p == tarc.Password.Length)
                    p = 0;
            }
            var input = new MemoryStream (data);
            if (null == tent || !tent.IsPacked)
                return input;
            return new LzssStream (input);
        }

        internal class IndexReader
        {
            ArcView         m_file;
            uint            m_packed_size;
            uint            m_unpacked_size;
            int             m_count;
            byte[]          m_index;
            Lazy<List<Entry>> m_dir;

            public byte[] Password { get; private set; }

            public IndexReader (ArcView file)
            {
                m_file = file;
                m_packed_size = m_file.View.ReadUInt32 (0x10);
                m_unpacked_size = m_file.View.ReadUInt32 (0x14);
                m_count = m_file.View.ReadInt32 (0x18);
                m_dir = new Lazy<List<Entry>> (() => new List<Entry> (m_count));
            }

            public List<Entry> ReadIndex ()
            {
                if (!IsSaneCount (m_count) || m_packed_size+0x20L > m_file.MaxOffset)
                    return null;
                m_index = new byte[m_unpacked_size];
                var readers = new Func<Stream, bool>[] {
                    ReadV0,
                    s => ReadV1 (s, 0x18),
                    s => ReadV1 (s, 0x10),
                };
                using (var input = m_file.CreateStream (0x20, m_packed_size))
                {
                    foreach (var read in readers)
                    {
                        try
                        {
                            if (read (input))
                                return m_dir.Value;
                        }
                        catch { /* ignore parse errors */ }
                        input.Position = 0;
                        if (m_dir.IsValueCreated)
                            m_dir.Value.Clear();
                    }
                    return null;
                }
            }

            bool ReadV1 (Stream input, int entry_size)
            {
                // NOTE CryptoStream will close an input stream
                using (var proxy = new InputProxyStream (input, true))
                using (var xored = new CryptoStream (proxy, new NotTransform(), CryptoStreamMode.Read))
                using (var lzss = new LzssStream (xored))
                    lzss.Read (m_index, 0, m_index.Length);

                int index_offset = Array.IndexOf<byte> (m_index, 0);
                if (-1 == index_offset || 0 == index_offset)
                    return false;
                Password = m_index.Take (index_offset++).ToArray();
                long base_offset = 0x20 + m_packed_size;

                for (int i = 0; i < m_count; ++i)
                {
                    var entry = new PackedEntry();
                    entry.Offset = LittleEndian.ToUInt32 (m_index, index_offset) + base_offset;
                    entry.Size   = LittleEndian.ToUInt32 (m_index, index_offset + 4);
                    entry.UnpackedSize = LittleEndian.ToUInt32 (m_index, index_offset + 8);
                    entry.IsPacked = entry.UnpackedSize != 0;
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    if (!entry.IsPacked)
                        entry.UnpackedSize = entry.Size;
                    int name_len = LittleEndian.ToInt32 (m_index, index_offset + 0xC);
                    if (name_len <= 0 || name_len > 0x100)
                        return false;
                    entry.Name = Encodings.cp932.GetString (m_index, index_offset+entry_size, name_len);
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    m_dir.Value.Add (entry);
                    index_offset += entry_size + name_len;
                }
                return true;
            }

            bool ReadV0 (Stream input)
            {
                long current_offset = 0x20 + m_packed_size;
                uint offset_table_size = (uint)m_count * 0x10;
                if (offset_table_size > m_file.View.Reserve (current_offset, offset_table_size))
                    return false;

                using (var lzss = new LzssStream (input, LzssMode.Decompress, true))
                    lzss.Read (m_index, 0, m_index.Length);

                for (int i = 0; i < m_index.Length; ++i)
                {
                    m_index[i] = (byte)(~m_index[i] - 5);
                }
                int index_offset = Array.IndexOf<byte> (m_index, 0);
                if (-1 == index_offset || 0 == index_offset)
                    return false;
                index_offset++;
//                Password = m_index.Take (index_offset++).ToArray();

                for (int i = 0; i < m_count && index_offset < m_index.Length; ++i)
                {
                    int name_end = Array.IndexOf<byte> (m_index, 0, index_offset);
                    if (-1 == name_end)
                        name_end = m_index.Length;
                    if (index_offset == name_end)
                        return false;
                    var entry = new PackedEntry();
                    entry.Offset = m_file.View.ReadUInt32 (current_offset);
                    entry.Size   = m_file.View.ReadUInt32 (current_offset+4);
                    entry.UnpackedSize = m_file.View.ReadUInt32 (current_offset+8);
                    entry.IsPacked = entry.UnpackedSize != 0;
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    entry.Name = Encodings.cp932.GetString (m_index, index_offset, name_end-index_offset);
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    m_dir.Value.Add (entry);
                    index_offset = name_end+1;
                    current_offset += 0x10;
                }
                return true;
            }
        }
    }
}
