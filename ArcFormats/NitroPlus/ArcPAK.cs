//! \file       ArcPAK.cs
//! \date       Sat May 21 00:12:14 2016
//! \brief      MAGI resource archive.
//
// Copyright (C) 2016-2018 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.Magi
{
    // this format is very similar to NitroPlus.PakOpener v3, but has no encryption.
    //
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/MAGI"; } }
        public override string Description { get { return "MAGI resource archive"; } }
        public override uint     Signature { get { return 3; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Signatures = new uint[] { 3, 4 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (0);
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (0xC);
            if (index_size < 2 || index_size > file.MaxOffset)
                return null;

            long base_offset = 0x118 + index_size;

            using (var mem = file.CreateStream (0x118, index_size))
            using (var z = new ZLibStream (mem, CompressionMode.Decompress))
            using (var index = new BinaryStream (z, file.Name))
            {
                var dir = new List<Entry> (count);
                string cur_dir = "";
                for (int i = 0; i < count; ++i)
                {
                    int name_length = index.ReadInt32();
                    if (name_length <= 0)
                        return null;
                    var name = index.ReadCString (name_length);
                    if (version > 3)
                    {
                        bool is_dir = index.ReadInt32() != 0;
                        if (is_dir)
                        {
                            cur_dir = name;
                            index.ReadInt64();
                            index.ReadInt32();
                            index.ReadInt64();
                            continue;
                        }
                        if (cur_dir.Length > 0)
                            name = Path.Combine (cur_dir, name);
                    }
                    var entry = Create<PackedEntry> (name);
                    entry.Offset        = index.ReadUInt32() + base_offset;
                    entry.UnpackedSize  = index.ReadUInt32();
                    index.ReadUInt32();
                    uint is_packed      = index.ReadUInt32();
                    uint packed_size    = index.ReadUInt32();
                    entry.IsPacked = is_packed != 0 && packed_size != 0;
                    if (entry.IsPacked)
                        entry.Size = packed_size;
                    else
                        entry.Size = entry.UnpackedSize;

                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size, entry.Name);
            var pentry = entry as PackedEntry;
            if (null != pentry && pentry.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
