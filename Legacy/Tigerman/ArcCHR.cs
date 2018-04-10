//! \file       ArcCHR.cs
//! \date       2018 Apr 10
//! \brief      Tigerman Project compound image.
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

namespace GameRes.Formats.Tigerman
{
    [Export(typeof(ArchiveFormat))]
    public class ChrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CHR/TIGERMAN"; } }
        public override string Description { get { return "Tigerman Project compound image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ChrOpener ()
        {
            Extensions = new string[] { "chr", "cls", "ev" };
            Signatures = new uint[] { 0x01B1, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = file.View.ReadUInt32 (0);
            if (base_offset >= file.MaxOffset || !file.View.AsciiEqual (base_offset, "ZT"))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            Func<int, uint, uint, Entry> create_entry = (i, offset, size) => new Entry {
                Name = string.Format ("{0}#{1}.ZIT", base_name, i),
                Type = "image",
                Offset = offset,
                Size = size,
            };
            dir.Add (create_entry (0, base_offset, file.View.ReadUInt32 (4)));
            uint index_offset = 12;
            while (index_offset + 0x24 <= base_offset)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset != 0)
                {
                    uint size   = file.View.ReadUInt32 (index_offset+4);
                    var entry = create_entry (dir.Count, offset, size);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x24;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
