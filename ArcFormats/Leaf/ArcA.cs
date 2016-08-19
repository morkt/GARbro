//! \file       ArcA.cs
//! \date       Wed Aug 17 17:32:54 2016
//! \brief      Leaf resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class AOpener : ArchiveFormat
    {
        public override string         Tag { get { return "A/Leaf"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public AOpener ()
        {
            Extensions = new string[] { "a" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0xAF1E != file.View.ReadUInt16 (0))
                return null;
            int count = file.View.ReadUInt16 (2);
            if (!IsSaneCount (count))
                return null;

            long base_offset = 4 + 0x20 * count;
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x17);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked = 0 != file.View.ReadByte (index_offset+0x17);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4);
            return new LzssStream (input);
        }
    }
}
