//! \file       ArcBOX.cs
//! \date       Sun Jan 17 14:45:34 2016
//! \brief      Aoi engine event script archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Security.Cryptography;

namespace GameRes.Formats.Aoi
{
    [Export(typeof(ArchiveFormat))]
    public class BoxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BOX"; } }
        public override string Description { get { return "Aoi engine script archive"; } }
        public override uint     Signature { get { return 0x42494F41; } } // 'AOIB'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "AOIBX10"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            int index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = file.View.ReadString (index_offset, 0x10),
                    Type = "script",
                    Offset = file.View.ReadUInt32 (index_offset+0x10),
                    Size = file.View.ReadUInt32 (index_offset+0x14),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new CryptoStream (input, new XorTransform (0xB2), CryptoStreamMode.Read);
        }
    }
}
