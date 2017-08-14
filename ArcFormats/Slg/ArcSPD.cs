//! \file       ArcSPD.cs
//! \date       2017 Aug 13
//! \brief      SLG system audio archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Slg
{
    [Export(typeof(ArchiveFormat))]
    public class SpdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SPD/SLG"; } }
        public override string Description { get { return "SLG system audio archive"; } }
        public override uint     Signature { get { return 0x504653; } } // 'SFP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.Name.HasExtension (".SPL"))
                return null;
            var index_name = Path.ChangeExtension (file.Name, ".SPL");
            if (!VFS.FileExists (index_name))
                return null;
            using (var idx = VFS.OpenView (index_name))
            {
                if (!idx.View.AsciiEqual (0, "SFP\0"))
                    return null;
                uint align = idx.View.ReadUInt32 (0xC);
                uint index_offset = 0x20;
                uint names_offset = idx.View.ReadUInt32 (index_offset);
                if (names_offset > idx.MaxOffset || names_offset <= index_offset)
                    return null;
                int count = (int)(names_offset - index_offset) / 0x10;
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint name_offset = idx.View.ReadUInt32 (index_offset);
                    var name = idx.View.ReadString (name_offset, (uint)(idx.MaxOffset - name_offset));
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = idx.View.ReadUInt32 (index_offset+4);
                    entry.Offset = (long)idx.View.ReadUInt32 (index_offset+8) * align;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x10;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
