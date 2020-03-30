//! \file       ArcPFD.cs
//! \date       2019 May 22
//! \brief      ADVG Script Interpreter System resource archive.
//
// Copyright (C) 2019 by morkt
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

// [060127][Artel] Horizont

namespace GameRes.Formats.Artel
{
    [Export(typeof(ArchiveFormat))]
    public class PfdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PFD"; } }
        public override string Description { get { return "Artel ADVG engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            int index_offset = 4;
            int data_offset = index_offset + count * 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x15);
                if (string.IsNullOrEmpty (name))
                    return null;
                var ext = file.View.ReadString (index_offset+0x15, 3);
                if (!string.IsNullOrEmpty (ext))
                    name = Path.ChangeExtension (name, ext);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x18);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x1C);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
