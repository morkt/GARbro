//! \file       ArcDLB.cs
//! \date       2023 Oct 13
//! \brief      AyPio resource archive (PC-98).
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

namespace GameRes.Formats.AyPio
{
    [Export(typeof(ArchiveFormat))]
    public class DlbOpener : ArchiveFormat
    {
        public override string         Tag => "DLB";
        public override string Description => "UK2 engine resource archive";
        public override uint     Signature => 0x64203C3C; // '<< d'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (3, "dlb file Ver1.00>>\0"))
                return null;
            int count = file.View.ReadInt16 (0x16);
            if (!IsSaneCount (count))
                return null;
            uint index_pos = 0x18;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_pos, 0xD);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos+0x0D);
                entry.Size   = file.View.ReadUInt32 (index_pos+0x11);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x15;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
