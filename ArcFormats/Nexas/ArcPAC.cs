//! \file       ArcNexas.cs
//! \date       Sat Mar 14 18:03:04 2015
//! \brief      NeXAS engine resource archives implementation.
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
        None2,
        Zstd,
        ZstdOrNone,
        NeedDecryptionOnly = 0xFDFD, // internal magic number
    }

    internal interface INexasIndexReader
    {
        Compression PackType { get; }

        List<Entry> Read ();
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
            Settings = new[] { PacEncoding };
        }

        readonly EncodingSetting PacEncoding = new EncodingSetting ("NexasEncodingCP", "DefaultEncoding");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PAC"))
                return null;

            List<Entry> dir = null;
            INexasIndexReader reader = new IndexReaderV1 (file, PacEncoding.Get<Encoding> ());
            try
            {
                dir = reader.Read ();
            }
            catch {}

            if (null == dir)
            {
                reader = new IndexReaderV0 (file);
                dir = reader.Read ();

                if (null == dir)
                    return null;
            }

            if (Compression.None == reader.PackType)
                return new ArcFile (file, this, dir);
            return new PacArchive (file, this, dir, reader.PackType);
        }

        internal sealed class IndexReaderV1 : INexasIndexReader
        {
            readonly ArcView    m_file;
            readonly int        m_count;
            readonly int        m_pack_type;
            readonly Encoding   m_encoding;

            const int MaxNameLength = 0x40;

            public Compression PackType { get { return (Compression)m_pack_type; } }

            public IndexReaderV1 (ArcView file, Encoding enc)
            {
                m_file = file;
                m_count = file.View.ReadInt32 (4);
                m_pack_type = file.View.ReadInt32 (8);
                m_encoding = enc;
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
                    success = ReadV0 ();
                }
                catch { /* ignore parse errors */ }
                if (!success && !ReadV1 ())
                    return null;
                return m_dir;
            }

            bool ReadV1 ()
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

            bool ReadV0 ()
            {
                using (var input = m_file.CreateStream ())
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
                m_dir.Clear ();
                for (int i = 0; i < m_count; ++i)
                {
                    var name = index.ReadCString (name_length, m_encoding);
                    if (string.IsNullOrWhiteSpace (name))
                        return false;
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset        = index.ReadUInt32 ();
                    entry.UnpackedSize  = index.ReadUInt32 ();
                    entry.Size          = index.ReadUInt32 ();
                    if (!entry.CheckPlacement (m_file.MaxOffset))
                        return false;
                    switch (m_pack_type)
                    {
                        case 1:
                        case 2:
                        case 3:
                        case 6:
                        {
                            entry.IsPacked = true;
                            break;
                        }
                        case 4:
                        case 7:
                        {
                            entry.IsPacked = entry.Size != entry.UnpackedSize;
                            break;
                        }
                    }
                    m_dir.Add (entry);
                }
                return true;
            }
        }

        internal sealed class IndexReaderV0 : INexasIndexReader
        {
            readonly ArcView m_file;
            readonly uint    m_header_size;

            public Compression PackType { get { return Compression.NeedDecryptionOnly; } }

            public IndexReaderV0 (ArcView file)
            {
                m_file = file;
                m_header_size = file.View.ReadUInt32 (3);
            }

            List<Entry> m_dir;

            public List<Entry> Read ()
            {
                m_dir = new List<Entry> ();
                using (var input = m_file.CreateStream ())
                {
                    input.Position = 7;
                    while (input.Position < m_header_size)
                    {
                        byte c;
                        List<byte> name_buffer = new List<byte> ();
                        while (true)
                        {
                            c = (byte)input.ReadByte ();
                            if (c == 0) break;
                            name_buffer.Add ((byte)~c);
                        }
                        var name = Binary.GetCString (name_buffer.ToArray (), 0);
                        if (string.IsNullOrWhiteSpace (name))
                            return null;
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = input.ReadUInt32 () + m_header_size;
                        entry.Size   = input.ReadUInt32 ();
                        if (!entry.CheckPlacement (m_file.MaxOffset))
                            return null;
                        m_dir.Add (entry);
                    }
                }
                return m_dir;
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pac = arc as PacArchive;
            var pent = entry as PackedEntry;

            if (null == pac)
                return input;
            if (Compression.NeedDecryptionOnly == pac.PackType)
            {
                using (input)
                {
                    var data = new byte[entry.Size];
                    input.Read (data, 0, data.Length);
                    for (int i = 0; i < Math.Min (3, data.Length); i++)
                        data[i] = (byte)~data[i];
                    return new BinMemoryStream (data, entry.Name);
                }
            }
            if (null == pent || !pent.IsPacked)
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
            case Compression.DeflateOrNone:
                return new ZLibStream (input, CompressionMode.Decompress);
            case Compression.Zstd:
            case Compression.ZstdOrNone:
            {
                using (input)
                {
                    var unpacked = ZstdDecompress (input, pent.UnpackedSize);
                    return new BinMemoryStream (unpacked, entry.Name);
                }
            }
            default:
                return input;
            }
        }

        static private byte[] HuffmanDecode (byte[] packed, int unpacked_size)
        {
            var dst = new byte[unpacked_size];
            var decoder = new HuffmanDecoder (packed, dst);
            return decoder.Unpack ();
        }

        static private byte[] ZstdDecompress (Stream s, uint unpackedSize)
        {
            using (var ds = new ZstdSharp.DecompressionStream (s))
            {
                var dst = new byte[unpackedSize];
                int decompressedSize = 0;

                while (decompressedSize < unpackedSize) 
                {
                    var count = ds.Read (dst, decompressedSize, (int)unpackedSize-decompressedSize);
                    if (0 == count)
                        return dst;
                    decompressedSize += count;
                }

                return dst;
            }
        }
    }
}
