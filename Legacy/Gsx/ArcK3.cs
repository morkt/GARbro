//! \file       ArcK3.cs
//! \date       2018 Feb 09
//! \brief      Toyo GSX resource archive.
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

// [000225][Light Plan] My Fairink Yousei Byakuya Monogatari

namespace GameRes.Formats.Gsx
{
    [Export(typeof(ArchiveFormat))]
    public class K3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "K3"; } }
        public override string Description { get { return "Toyo GSX resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "K3"))
                return null;
            int count = file.View.ReadInt32 (2);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 6;
            long base_offset = index_offset + count * 0x40;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size = file.View.ReadUInt32 (index_offset+4);
                int type = file.View.ReadInt32 (index_offset+0xC);
                var name = file.View.ReadString (index_offset+0x20, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x40;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
