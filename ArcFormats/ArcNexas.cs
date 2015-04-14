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
using GameRes.Formats.Strings;
using GameRes.Utility;
using ZLibNet;

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
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            int pack_type = file.View.ReadInt32 (8);
            uint index_size = file.View.ReadUInt32 (file.MaxOffset-4);
            if (index_size >= file.MaxOffset)
                return null;

            byte[] index_packed = new byte[index_size];
            file.View.Read (file.MaxOffset-4-index_size, index_packed, 0, index_size);
            for (int i = 0; i < index_packed.Length; ++i)
                index_packed[i] = (byte)~index_packed[i];

            var index = HuffmanDecode (index_packed, count*0x4c);
            var dir = new List<Entry> (count);
            int offset = 0;
            for (int i = 0; i < count; ++i, offset += 0x4c)
            {
                int name_length = 0;
                while (name_length < 0x40 && 0 != index[offset+name_length])
                    name_length++;
                if (0 == name_length)
                    continue;
                var name = Encodings.cp932.GetString (index, offset, name_length);
                var entry = new PackedEntry
                {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = LittleEndian.ToUInt32 (index, offset+0x40),
                    UnpackedSize = LittleEndian.ToUInt32 (index, offset+0x44),
                    Size = LittleEndian.ToUInt32 (index, offset+0x48),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = pack_type != 0 && entry.UnpackedSize != entry.Size;
                dir.Add (entry);
            }
            if (0 == pack_type)
                return new ArcFile (file, this, dir);
            return new PacArchive (file, this, dir, (Compression)pack_type);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pac = arc as PacArchive;
            var pent = entry as PackedEntry;
            if (null == pac || Compression.None == pac.PackType ||
                null == pent || pent.Size == pent.UnpackedSize)
                return input;
            switch (pac.PackType)
            {
            case Compression.Lzss:
                using (input)
                using (var reader = new LzssReader (input, (int)pent.Size, (int)pent.UnpackedSize))
                {
                    reader.Unpack();
                    return new MemoryStream (reader.Data, false);
                }
            case Compression.Huffman:
                using (input)
                {
                    var packed = new byte[entry.Size];
                    input.Read (packed, 0, packed.Length);
                    var unpacked = HuffmanDecode (packed, (int)pent.UnpackedSize);
                    return new MemoryStream (unpacked, 0, (int)pent.UnpackedSize, false);
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
