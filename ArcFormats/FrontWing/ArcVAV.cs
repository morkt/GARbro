//! \file       ArcVAV.cs
//! \date       2018 Mar 10
//! \brief      FrontWing ADV System resource archive.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.FrontWing
{
    internal class VavEntry : PackedEntry
    {
        public int  Compression;
    }

    internal class VavArchive : ArcFile
    {
        public int  Version;

        public VavArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/vav"; } }
        public override string Description { get { return "FrontWing ADV System resource archive"; } }
        public override uint     Signature { get { return 0x766176; } } // 'vav'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (4);
            if (version != 100 && version != 200 && version != 201)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            bool is_voice = Path.GetFileNameWithoutExtension (file.Name).Equals ("voice", StringComparison.OrdinalIgnoreCase);
            uint index_offset = file.View.ReadUInt32 (0xC);
            uint name_size = version < 200 ? 0x10u : 0x20u;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_size);
                var entry = Create<VavEntry> (name);
                index_offset += name_size;
                entry.Size         = file.View.ReadUInt32 (index_offset);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+4);
                entry.Offset       = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Compression  = file.View.ReadInt32 (index_offset+0xC);
                entry.IsPacked     = (entry.Compression & 0x90) != 0;
                if (is_voice)
                    entry.Type = "audio";
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new VavArchive (file, this, dir, version);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var vent = entry as VavEntry;
            if (null == vent)
                return base.OpenEntry (arc, entry);
            var varc = arc as VavArchive;
            bool old_version = varc != null && varc.Version < 200;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int data_length = data.Length;
            if ((vent.Compression & 0x80) != 0)
            {
                var output = new byte[vent.UnpackedSize];
                data_length = UnpackHuffman (data, data_length, output);
                data = output;
            }
            if ((vent.Compression & 0x10) != 0)
            {
                var output = new byte[vent.UnpackedSize];
                UnpackRle (data, data_length, output);
                data = output;
            }
            DecryptEntry (data, vent.Compression & 0xF, old_version);
            return new BinMemoryStream (data);
        }

        void DecryptEntry (byte[] data, int start, bool old_version = false)
        {
            if (start > 0)
            {
                for (int i = start; i < data.Length; ++i)
                    data[i] ^= data[i-start];
            }
            else
            {
                int data_length = old_version ? 1 : data.Length;
                for (int i = 0; i < data_length; ++i)
                    data[i] ^= 0x55;
            }
        }

        int UnpackRle (byte[] input, int input_length, byte[] output)
        {
            int src = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                byte rle = input[src++];
                int count = Math.Min (rle & 0x7F, output.Length - dst);
                if (0 != (rle & 0x80))
                {
                    byte v = input[src++];
                    while (count --> 0)
                        output[dst++] = v;
                }
                else
                {
                    Buffer.BlockCopy (input, src, output, dst, count);
                    src += count;
                    dst += count;
                }
            }
            return dst;
        }

        int UnpackHuffman (byte[] input, int input_length, byte[] output)
        {
            using (var mem = new MemoryStream (input, 0, input_length))
            {
                var tree = new HuffmanNode[0x201];
                ushort root = BuildHuffmanTree (tree, mem);
                using (var bits = new MsbBitStream (mem))
                {
                    int dst = 0;
                    for (;;)
                    {
                        ushort symbol = root;
                        while (symbol > 0x100)
                        {
                            int bit = bits.GetBits (1);
                            if (-1 == bit)
                                return dst;
                            if (bit != 0)
                                symbol = tree[symbol].RChild;
                            else
                                symbol = tree[symbol].LChild;
                        }
                        if (0x100 == symbol)
                            return dst;
                        output[dst++] = (byte)symbol;
                    }
                }
            }
        }

        internal struct HuffmanNode
        {
            public int      Weight;
            public ushort   LChild;
            public ushort   RChild;
        }

        ushort BuildHuffmanTree (HuffmanNode[] tree, Stream input)
        {
            for (int i = 0; i < 0x100; ++i)
            {
                tree[i].Weight = input.ReadByte() ^ 0x55;
            }
            ushort root = 0x100;
            tree[root].Weight = 1;
            ushort lhs = 0x200;
            ushort rhs = 0x200;
            for (;;)
            {
                int lmin = 0x10000;
                int rmin = 0x10000;
                for (ushort i = 0; i < 0x201; ++i)
                {
                    int w = tree[i].Weight;
                    if (w != 0 && w < rmin)
                    {
                        rmin = lmin;
                        lmin = w;
                        rhs = lhs;
                        lhs = i;
                    }
                }
                if (rmin == 0x10000 || lmin == 0x10000 || lmin == 0 || rmin == 0)
                    break;
                ++root;
                tree[root].LChild = lhs;
                tree[root].RChild = rhs;
                tree[root].Weight = rmin + lmin;
                tree[lhs].Weight = 0;
                tree[rhs].Weight = 0;
            }
            return root;
        }
    }
}
