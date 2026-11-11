//! \file       ArcKLZ.cs
//! \date       2026-09-07
//! \brief      SVIU System resource archive.
//
// Copyright (C) 2026 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Sviu
{
    [Export(typeof(ArchiveFormat))]
    public class KlzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KLZ"; } }
        public override string Description { get { return "SVIU System resource archive"; } }
        public override uint     Signature { get { return 0x355A4C4B; } } // 'KLZ5'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = (uint)count * 0x34u + 0x10u;

            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Size = file.View.ReadUInt32 (index_offset + 0x20);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset + 0x24);
                entry.Offset = file.View.ReadUInt32 (index_offset + 0x2C) + data_offset;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x34;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);

            if (pent.IsPacked)
            {
                var data = new byte[pent.UnpackedSize];
                PkzOpener.LzUnpack (input, data, 0);
                if (data.AsciiEqual (0, "SVS1"))
                    data = PkzOpener.UnpackScript (data);
                return new BinMemoryStream (data);
            }
            return input;
        }
    }
}
