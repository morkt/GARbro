//! \file       ArcScn.cs
//! \date       2023 Aug 28
//! \brief      Software House Parsley scripts archive.
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

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class ScnDatOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/SCN"; }
        public override string Description { get => "Software House Parsley scenario archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "scn.dat"))
                return null;
            uint base_offset = 0x1400;
            uint index_pos = 0;
            var dir = new List<Entry>();
            while (index_pos < base_offset && file.View.ReadByte (index_pos) != 0)
            {
                var name = file.View.ReadString (index_pos, 0x20);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos+0x20) + base_offset;
                entry.Size   = file.View.ReadUInt32 (index_pos+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x28;
            }
            if (dir.Count == 0)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
