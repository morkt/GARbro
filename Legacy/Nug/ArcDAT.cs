//! \file       ArcDAT.cs
//! \date       2023 Sep 10
//! \brief      NUG System resource archive.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Nug
{
    [Export(typeof(ArchiveFormat))]
    public class FwaOpener : ArchiveFormat
    {
        public override string         Tag => "DAT/FWA";
        public override string Description => "Frontwing resource archive";
        public override uint     Signature => 0x46574131; // '1AWF'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            long index_offset = file.View.ReadUInt32 (0x10) + count * 4;
            uint index_size = (uint)count * 0x40u;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint entry_size = file.View.ReadUInt32 (index_offset);
                if (entry_size < 0x40)
                    return null;
                var name = file.View.ReadString (index_offset+0x10, 0x30);
                var entry = Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = entry.Size;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            var offset = entry.Offset;
            uint signature = arc.File.View.ReadUInt32 (offset);
            if (!pent.IsPacked && entry.Size > 0x20)
            {
                if (signature == 0x46574353) // 'SCWF'
                {
                    pent.IsPacked = true;
                    pent.UnpackedSize = arc.File.View.ReadUInt32 (offset+0x10);
                    entry.Offset += 0x20;
                    entry.Size -= 0x20;
                }
                else if (signature == 0x46574343) // 'CCWF'
                {
                    entry.Offset += 0x20;
                    entry.Size = arc.File.View.ReadUInt32 (offset+0x10);
                }
            }
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }
    }
}
