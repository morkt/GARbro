//! \file       ArcDM.cs
//! \date       2023 Oct 24
//! \brief      D-Motion engine resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.DMotion
{
    internal class ExtEntry : Entry
    {
        public int Count;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "256/DMOTION";
        public override string Description => "D-Motion engine resource archive";
        public override uint     Signature => 0x4B434150; // 'PACK'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "FILE100DATA"))
                return null;
            if (!file.View.AsciiEqual (0x10, @".\\\"))
                return null;
            int ext_count = file.View.ReadUInt16 (0x16);
            long index_pos = file.View.ReadUInt32 (0x18);
            int total_count = 0;
            var ext_dir = new List<ExtEntry> (ext_count);
            for (int i = 0; i < ext_count; ++i)
            {
                var ext = new ExtEntry {
                    Name   = file.View.ReadString (index_pos, 4),
                    Count  = file.View.ReadUInt16 (index_pos+6),
                    Offset = file.View.ReadUInt32 (index_pos+8),
                    Size   = file.View.ReadUInt32 (index_pos+12),
                };
                ext_dir.Add (ext);
                total_count += ext.Count;
                index_pos += 0x10;
            }
            if (!IsSaneCount (total_count))
                return null;

            var dir = new List<Entry> (total_count);
            foreach (var ext in ext_dir)
            {
                index_pos = ext.Offset;
                for (int i = 0; i < ext.Count; ++i)
                {
                    var name = file.View.ReadString (index_pos, 8).TrimEnd() + ext.Name;
                    var entry = Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_pos+8);
                    entry.Size   = file.View.ReadUInt32 (index_pos+12);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_pos += 0x10;
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
