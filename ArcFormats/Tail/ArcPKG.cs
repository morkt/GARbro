//! \file       ArcPKG.cs
//! \date       2018 Dec 20
//! \brief      Archive's resource container.
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

// [000602][Archive] Marchen Maid Jigoku

namespace GameRes.Formats.Tail
{
    [Export(typeof(ArchiveFormat))]
    public class PkgOpener : CafOpener
    {
        public override string         Tag { get { return "PKG/ARCHIVE"; } }
        public override string Description { get { return "Archive's resource container"; } }
        public override uint     Signature { get { return 0x20474B50; } } // 'PKG '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 8;
            string default_type = null;
            if (base_name.Equals ("cg", StringComparison.OrdinalIgnoreCase))
                default_type = "image";
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var offset = file.View.ReadUInt32 (index_offset);
                Entry entry;
                if (default_type != null)
                    entry = new Entry { Name = name, Type = default_type, Offset = offset };
                else
                    entry = AutoEntry.Create (file, offset, name);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
