//! \file       ArcPAC.cs
//! \date       Fri Feb 20 13:51:48 2015
//! \brief      PAC archive implementation.
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

using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;

namespace GameRes.Formats.Riddle
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC1"; } }
        public override string Description { get { return "Riddle Soft resource archive"; } }
        public override uint     Signature { get { return 0x31434150; } } // 'PAC1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            long index_offset = 8;
            uint base_offset = (uint)(count*0x20 + 8);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                uint size = file.View.ReadUInt32 (index_offset+0x10);
                entry.Offset = base_offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                base_offset += size;
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
