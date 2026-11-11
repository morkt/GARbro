//! \file       ArcMOV.cs
//! \date       2026-09-07
//! \brief      SVIU System video format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Sviu
{
    [Export(typeof(ArchiveFormat))]
    public class MovOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MOV"; } }
        public override string Description { get { return "SVIU System video format"; } }
        public override uint     Signature { get { return 0x31564F4D; } } // 'MOV1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MovOpener ()
        {
            Extensions = new string[] { "mov", "jmv" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (0x2C);
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            int index_offset = 0x30;
            Entry entry;
            for (int i = 0; i < count; ++i)
            {
                entry = new Entry {
                    Name = string.Format ("{0}#{1:D05}", base_name, i),
                    Size = file.View.ReadUInt32 (index_offset + 4),
                    Offset = file.View.ReadUInt32 (index_offset + 0x10) + data_offset,
                    Type = "image"
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            entry = new Entry {
                Name = base_name + "#audio",
                Size = file.View.ReadUInt32 (index_offset + 4),
                Offset = file.View.ReadUInt32 (index_offset + 0x10) + data_offset,
                Type = "audio"
            };
            if (!entry.CheckPlacement (file.MaxOffset))
                return null;
            dir.Add (entry);
            return new ArcFile (file, this, dir);
        }
    }
}
