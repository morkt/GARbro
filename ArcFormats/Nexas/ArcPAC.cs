//! \file       ArcNexas.cs
//! \date       Sat Mar 14 18:03:04 2015
//! \brief      NeXAS enginge resource archives implementation.
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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Compression;
using GameRes.Formats.Strings;
using GameRes.Utility;

namespace GameRes.Formats.NeXAS
{
    public enum Compression
    {
        None,
        Lzss,
        Huffman,
        Deflate,
        DeflateOrNone,
    }

    public class PacArchive : ArcFile
    {
        public readonly Compression PackType;

        public PacArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, Compression type)
            : base (arc, impl, dir)
        {
            PackType = type;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC"; } }
        public override string Description { get { return "NeXAS engine resource archive"; } }
        public override uint     Signature { get { return 0x00434150; } } // 'PAC\000'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            Signatures = new uint[] { 0x00434150, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PAC") || 'K' == file.View.ReadByte (3))
                return null;
            var reader = new IndexReader (file);
            var dir = reader.Read();
            if (null == dir)
                return null;

            if (Compression.None == reader.PackType)
                return new ArcFile (file, this, dir);
            return new PacArchive (file, this, dir, reader.PackType);
        }

        internal sealed class IndexReader
        {
            ArcView     m_file;
            int         m_count;
            int         m_pack_type;

            const int MaxNameLength = 0x40;

            public Compression PackType { get { return (Compression)m_pack_type; } }

            public IndexReader (ArcView file)
            {
                m_file = file;
                m_count = file.View.ReadInt32 (4);
                m_pack_type = file.View.ReadInt32 (8);
            }

            List<Entry> m_dir;
            
            public List<Entry> Read ()
            {
                if (!IsSaneCount (m_count))
                    return null;
                m_dir = new List<Entry> (m_count);
                bool success = false;
                try
                {
                    success = ReadOld();
                }
                catch { /* ignore parse errors */ }
                if (!success && !ReadNew())
                    return null;
                return m_dir;
            }

            bool ReadNew ()
            {
                uint index_size = m_file.View.ReadUInt32 (m_file.MaxOffset-4);
                int unpacked_size = m_count*0x4C;
                if (index_size >= m_file.MaxOffset || index_size > unpacked_size*2)
                    return false;

                var index_packed = m_file.View.ReadBytes (m_file.MaxOffset-4-index_size, index_size);
                for (int i = 0; i < index_packed.Length; ++i)
                    index_packed[i] = (byte)~index_packed[i];

                var index = HuffmanDecode (index_packed, unpacked_size);
                using (var input = new BinMemoryStream (index))
                    return ReadFromStream (input, 0x40);
            }

            bool ReadOld ()
            {
                using (var input = m_file.CreateStream())
                {
                    input.Position = 0xC;
                    if (ReadFromStream (input, 0x20))
                        return true;
                    input.Position = 0xC;
                    return ReadFromStream (input, 0x40);
                }
            }

            bool ReadFromStream (IBinaryStream index, int name_length)
            {
                m_dir.Clear();
                for (int i = 0; i < m_count; ++i)
                {
                    var name = index.ReadCString (name_length);
                    if (string.IsNullOrWhiteSpace (name))
                        return false;
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset        = index.ReadUInt32();
                    entry.UnpackedSize  = index.ReadUInt32();
                    entry.Size          = index.ReadUInt32();
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    entry.IsPacked = m_pack_type != 0 && (m_pack_type != 4 || entry.Size != entry.UnpackedSize);
                    m_dir.Add (entry);
                }
                return true;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pac = arc as PacArchive;
            var pent = entry as PackedEntry;
            if (null == pac || null == pent || !pent.IsPacked)
                return input;
            switch (pac.PackType)
            {
            case Compression.Lzss:
                return new LzssStream (input);

            case Compression.Huffman:
                using (input)
                {
                    var packed = new byte[entry.Size];
                    input.Read (packed, 0, packed.Length);
                    var unpacked = HuffmanDecode (packed, (int)pent.UnpackedSize);
                    return new BinMemoryStream (unpacked, 0, (int)pent.UnpackedSize, entry.Name);
                }
            case Compression.Deflate:
            default:
                return new ZLibStream (input, CompressionMode.Decompress);
            }
        }

        static private byte[] HuffmanDecode (byte[] packed, int unpacked_size)
        {
            var dst = new byte[unpacked_size];
            var decoder = new HuffmanDecoder (packed, dst);
            return decoder.Unpack();
        }
    }
}
