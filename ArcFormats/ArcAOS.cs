//! \file       ArcAOS.cs
//! \date       Tue Aug 04 06:00:39 2015
//! \brief      LiLiM resource archive implementation.
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
using GameRes.Utility;

namespace GameRes.Formats.Lilim
{
    [Export(typeof(ArchiveFormat))]
    public class AosOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AOS"; } }
        public override string Description { get { return "LiLiM/Le.Chocolat engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        static readonly byte[] IndexLink = Enumerable.Repeat<byte> (0xff, 0x10).ToArray();
        static readonly byte[] IndexEnd  = Enumerable.Repeat<byte> (0, 0x10).ToArray();

        public override ArcFile TryOpen (ArcView file)
        {
            if (0 == file.View.ReadByte (0))
                return null;
            uint first_offset = file.View.ReadUInt32 (0x10);
            if (first_offset >= file.MaxOffset || 0 != (first_offset & 0x1F))
                return null;
            var name_buf = new byte[0x10];
            if (0x10 != file.View.Read (first_offset, name_buf, 0, 0x10))
                return null;
            if (!name_buf.SequenceEqual (IndexLink) && !name_buf.SequenceEqual (IndexEnd))
                return null;

            uint current_offset = 0;
            var dir = new List<Entry> (0x3E);
            for (;;)
            {
                if (0x10 != file.View.Read (current_offset, name_buf, 0, 0x10))
                    break;
                if (name_buf.SequenceEqual (IndexLink))
                {
                    uint next_offset = file.View.ReadUInt32 (current_offset+0x10);
                    current_offset += 0x20 + next_offset;
                    if (current_offset >= file.MaxOffset)
                        break;
                }
                else
                {
                    int name_length = Array.IndexOf<byte> (name_buf, 0);
                    if (0 == name_length)
                        break;
                    if (-1 == name_length)
                        name_length = name_buf.Length;
                    var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.CreateEntry (name);
                    entry.Offset = file.View.ReadUInt32 (current_offset+0x10);
                    entry.Size   = file.View.ReadUInt32 (current_offset+0x14);
                    current_offset += 0x20;
                    entry.Offset += current_offset;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith (".scr", StringComparison.InvariantCultureIgnoreCase))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset);
            var packed = new byte[entry.Size-4];
            arc.File.View.Read (entry.Offset+4, packed, 0, (uint)packed.Length);
            var unpacked = new byte[unpacked_size];

            var decoder = new HuffmanDecoder (packed, unpacked);
            decoder.Unpack();
            return new MemoryStream (unpacked);
        }
    }
}
