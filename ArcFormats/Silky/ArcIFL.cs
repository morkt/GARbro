//! \file       ArcIFL.cs
//! \date       Sun Apr 12 20:47:04 2015
//! \brief      IFLS archive implementation.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class IflOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IFL"; } }
        public override string Description { get { return "Silky's engine resource archive"; } }
        public override uint     Signature { get { return 0x534c4649; } } // 'IFLS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (data_offset <= 12 || data_offset >= file.MaxOffset
                || count <= 0 || count > 0xfffff)
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 12;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 8
                || !entry.Name.EndsWith (".snc", StringComparison.InvariantCultureIgnoreCase)
                || !arc.File.View.AsciiEqual (entry.Offset, "CMP_"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            int unpacked_size = arc.File.View.ReadInt32 (entry.Offset+4);
            using (var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8))
            using (var lzss = new LzssReader (input, (int)(entry.Size - 8), unpacked_size))
            {
                lzss.FrameFill = 0x20;
                lzss.Unpack();
                return new MemoryStream (lzss.Data);
            }
        }
    }
}
