//! \file       ArcIPAC.cs
//! \date       Sat Jul 23 14:03:35 2016
//! \brief      IPAC resource archive.
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

namespace GameRes.Formats.BaseUnit
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/IPAC"; } }
        public override string Description { get { return "IPAC resource archive"; } }
        public override uint     Signature { get { return 0x43415049; } } // 'IPAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                var entry = Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x24);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x28);
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
                if (!arc.File.View.AsciiEqual (entry.Offset, "IEL1"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            return new LzssStream (input);
        }
    }
}
