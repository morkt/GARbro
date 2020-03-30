//! \file       ArcMIK01.cs
//! \date       2018 Jan 21
//! \brief      MAIKA resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Maika
{
    [Export(typeof(ArchiveFormat))]
    public class MikOpener : Mk2Opener
    {
        public override string         Tag { get { return "DAT/MIK01"; } }
        public override string Description { get { return "MAIKA resource archive"; } }
        public override uint     Signature { get { return 0x304B494D; } } // 'MIK01'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MikOpener ()
        {
            Signatures = new uint[] { 0x304B494D, 0x30475355 }; // 'MIK01', 'USG01'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "1\x1A\0"))
                return null;
            int count = file.View.ReadInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xA);
            uint offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += entry.Size;
                index_offset += 0x10;
            }
            return GetArchive (file, dir);
        }
    }
}
