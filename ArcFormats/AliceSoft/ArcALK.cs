//! \file       ArcALK.cs
//! \date       Mon Apr 25 20:00:37 2016
//! \brief      AliceSoft System 4 engine resource archive.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AlkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ALK"; } }
        public override string Description { get { return "AliceSoft System 4 resource archive"; } }
        public override uint     Signature { get { return 0x304B4C41; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 8;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var offset = file.View.ReadUInt32 (index_offset);
                var size   = file.View.ReadUInt32 (index_offset+4);
                if (0 != size)
                {
                    var name = string.Format ("{0}#{1:D4}", base_name, i);
                    var entry = AutoEntry.Create (file, offset, name);
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
