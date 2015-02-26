//! \file       ArcADPACK.cs
//! \date       Sun Feb 15 09:25:28 2015
//! \brief      ADPACK32 archive format implementation.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.AdPack
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ADPACK32"; } }
        public override string Description { get { return "Active Soft resource archive"; } }
        public override uint     Signature { get { return 0x41504441; } } // "ADPA"
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "CK32"))
                return null;
            int count = file.View.ReadInt32 (12) - 1;
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = (uint)(0x20 * count);
            if (index_size > file.View.Reserve (0x10, index_size))
                return null;
            var dir = new List<Entry> ((int)count);
            long index_offset = 0x10;
            for (uint i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x18);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                uint offset = file.View.ReadUInt32 (index_offset+0x1c);
                uint next_offset = file.View.ReadUInt32 (index_offset+0x20+0x1c);
                entry.Size = next_offset - offset;
                entry.Offset = offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
