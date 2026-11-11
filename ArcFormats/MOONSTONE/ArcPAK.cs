//! \file       ArcPAK.cs
//! \date       2025 May 03
//! \brief      MOONSTONE PAK resource archive.
//
// Copyright (C) 2015-2018 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Moonstone
{
    [Export(typeof(ArchiveFormat))]
    public class MtsPakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/DATATOP"; } }
        public override string Description { get { return "MOONSTONE resource archive"; } }
        public override uint     Signature { get { return 0x41544144; } } // 'DATA$TOP'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "$TOP"))
                return null;
            int count = file.View.ReadInt32 (0x38) - 1;
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x40;
            long base_offset = index_offset * (count + 1);
            var name_buf = new byte[0x30];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buf, 0, 0x30);
                var name = Binary.GetCString (name_buf, 0);
                var entry = Create<Entry> (name);
                entry.Size  = file.View.ReadUInt32 (index_offset + 0x38);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset + 0x34);
                // actually there are 2 duplicate offsets for each file in the index 
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
