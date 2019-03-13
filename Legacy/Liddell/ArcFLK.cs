//! \file       ArcFLK.cs
//! \date       2019 Mar 12
//! \brief      Liddell resource archive.
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
using GameRes.Utility;

// [020726][Liddell] Garasu no Yakata ~Kimi ga Inai Yoru~

namespace GameRes.Formats.Liddell
{
    [Export(typeof(ArchiveFormat))]
    public class FlkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FLK"; } }
        public override string Description { get { return "Liddell resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".flk"))
                return null;
            uint index_pos = 0;
            var buffer = file.View.ReadBytes (index_pos, 0x10);
            int base_offset = buffer[3] << 8;
            int next_offset = ((buffer[1] << 8 | buffer[0]) << 4) + base_offset;
            var dir = new List<Entry>();
            while (buffer[4] != 0)
            {
                uint tail_size = buffer[2];
                var name = Binary.GetCString (buffer, 4, 12);
                var entry = Create<Entry> (name);
                entry.Offset = next_offset;
                index_pos += 0x10;
                if (file.View.Read (index_pos, buffer, 0, 0x10) != 0x10)
                    return null;
                next_offset = ((buffer[3] << 16 | buffer[1] << 8 | buffer[0]) << 4) + base_offset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (tail_size != 0)
                    entry.Size += tail_size - 0x10;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
