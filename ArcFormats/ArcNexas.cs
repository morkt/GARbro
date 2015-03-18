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
            for (int i = 0; i < count; ++i)
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
                offset += 0x4c;
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
                return input;  // LZSS compression not implemented
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

    internal class HuffmanDecoder
    {
        byte[] m_src;
        byte[] m_dst;

        ushort[] lhs = new ushort[512];
        ushort[] rhs = new ushort[512];
        ushort t1032 = 256;

        int t0;
        int remaining;
        int t8;
        int t12;

        public HuffmanDecoder (byte[] src, byte[] dst)
        {
            m_src = src;
            m_dst = dst;
            t0 = 0;
            remaining = src.Length;
            t8 = 0;
            t12 = 0;
        }

        public byte[] Unpack ()
        {
            int a2 = 0;
            t1032 = 256;
            ushort v3 = sub_401540();
            ushort v13 = v3; // [sp+0h] [bp-4h]@1
            while (a2 < m_dst.Length)
            {
                ushort v6 = v3;
                if ( v6 >= 0x100u )
                {
                    do
                    {
                        while ( 0 == t8 )
                        {
                            int v8 = m_src[t0++];
                            int v9 = t12;
                            t8 += 8;
                            --remaining;
                            t12 = v8 | (v9 << 8);
                        }
                        int v10 = t12;
                        int v11 = --t8;
                        uint v12 = (uint)t12;
                        t12 = v10 & ~(-1 << v11);
                        if ( 0 != (((-1 << v11) & v12) >> v11) )
                            v6 = rhs[v6];
                        else
                            v6 = lhs[v6];
                    }
                    while ( v6 >= 0x100u );
                    v3 = v13;
                }
                m_dst[a2++] = (byte)v6;
            }
            return m_dst;
        }

        ushort sub_401540()
        {
            if ( 0 != GetBits (1) )
            {
                ushort v4 = t1032++;
                lhs[v4] =  sub_401540();
                rhs[v4] =  sub_401540();
                return v4;
            }
            else
            {
                return (ushort)GetBits (8);
            }
        }

        uint GetBits (int n)
        {
            while ( n > t8 )
            {
                int v4 = m_src[t0++];
                --remaining;
                t12 = v4 | (t12 << 8);
                t8 += 8;
            }
            int v7 = t12;
            uint v8 = (uint)t12;
            t8 -= n;
            t12 = v7 & ~(-1 << t8);
            return (uint)(((-1 << t8) & v8) >> t8);
        }
    }
}
