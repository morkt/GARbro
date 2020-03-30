//! \file       ArcPKD.cs
//! \date       2019 Jun 25
//! \brief      Zone resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Zone
{
    // [991118][Zone] Guren

    [Export(typeof(ArchiveFormat))]
    public class PkdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PKD/ZONE"; } }
        public override string Description { get { return "Zone resource archive"; } }
        public override uint     Signature { get { return 1; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pkd"))
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (0xC);
            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
