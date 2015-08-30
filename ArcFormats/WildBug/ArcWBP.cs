//! \file       ArcWBP.cs
//! \date       Thu Jul 09 17:02:03 2015
//! \brief      Wild Bug's resource archive format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.WildBug
{
    [Export(typeof(ArchiveFormat))]
    public class WbpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WBP"; } }
        public override string Description { get { return "Wild Bug's engine resource archive"; } }
        public override uint     Signature { get { return 0x46435241; } } // 'ARCF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "ARCFORM3 WBUG "))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x14);
            uint index_size   = file.View.ReadUInt32 (0x18);
            uint data_offset  = file.View.ReadUInt32 (0x1C);
            if (data_offset < index_offset || data_offset > file.MaxOffset)
                return null;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (index_offset+9);
                string name = file.View.ReadString (index_offset+0x14, name_length);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += name_length + 0x14;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
