//! \file       ArcX.cs
//! \date       2018 Jan 25
//! \brief      Akatombo resource archive.
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

// [980515][Akatonbo] Succubus ~Ochita Tenshi~

namespace GameRes.Formats.Akatombo
{
    [Export(typeof(ArchiveFormat))]
    public class XOpener : ArchiveFormat
    {
        public override string         Tag { get { return "X/AKATOMBO"; } }
        public override string Description { get { return "Akatombo resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 2;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            if (next_offset != count * 4 + 6)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name   = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = next_offset,
                };
                next_offset = file.View.ReadUInt32 (index_offset);
                if (next_offset < entry.Offset || next_offset > file.MaxOffset)
                    return null;
                entry.Size = (uint)(next_offset - entry.Offset);
                dir.Add (entry);
            }
            if (next_offset != file.MaxOffset)
                return null;
            foreach (var entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset);
                if ((signature & 0xFFFF) == 0x4246) // 'FB'
                    entry.Type = "image";
                else
                    entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }
    }
}
