//! \file       ArcTTD.cs
//! \date       2018 Sep 02
//! \brief      Melonpan resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [030829][Melonpan] Himawari no Natsu ~Boku ga Eranda Ashita~

namespace GameRes.Formats.Melonpan
{
    [Export(typeof(ArchiveFormat))]
    public class TtdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TTD"; } }
        public override string Description { get { return "Melonpan resource archive"; } }
        public override uint     Signature { get { return 0x574357; } } // 'WCW'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 12;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size   = file.View.ReadUInt32 (index_offset);
                uint offset = file.View.ReadUInt32 (index_offset+4);
                var name = file.View.ReadString (index_offset+0xC, 0x20);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "DSFF"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            var lzss = new LzssStream (input);
            lzss.Config.FrameInitPos = 0xFF0;
            return lzss;
        }
    }
}
