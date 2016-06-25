//! \file       ArcPAK.cs
//! \date       Sat Jun 25 21:39:46 2016
//! \brief      Black Rainbow resource archive.
//
// Copyright (C) 2016 by morkt
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
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/MELTY"; } }
        public override string Description { get { return "BlackRainbow/Melty resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 8)
                return null;
            int count = file.View.ReadInt32 (file.MaxOffset-8);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (file.MaxOffset-4);
            if (index_size >= file.MaxOffset-8)
                return null;

            long index_offset = file.MaxOffset - 8 - index_size;
            using (var input = file.CreateStream (index_offset, index_size))
            using (var index = new BinaryReader (input, Encoding.Unicode))
            {
                char[] name_buffer = new char[0x40];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32();
                    uint packed_size = index.ReadUInt32();
                    uint unpacked_size = index.ReadUInt32();
                    int name_length = index.ReadInt32();
                    if (name_length <= 0 || name_length > 0x100 )
                        return null;
                    if (name_length > name_buffer.Length)
                        name_buffer = new char[name_length];
                    if (name_length != index.Read (name_buffer, 0, name_length))
                        return null;
                    var name = new string (name_buffer, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size = packed_size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.IsPacked = uint.MaxValue != unpacked_size;
                    entry.UnpackedSize = entry.IsPacked ? unpacked_size : packed_size;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
