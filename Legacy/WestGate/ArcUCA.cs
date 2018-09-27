//! \file       ArcUCA.cs
//! \date       2017 Dec 15
//! \brief      West Gate resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.WestGate
{
    [Export(typeof(ArchiveFormat))]
    public class UcaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "UCA"; } }
        public override string Description { get { return "West Gate graphics archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public UcaOpener ()
        {
            Extensions = new[] { "uca", "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (0) != 0)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count) || count != file.View.ReadInt32 (8))
                return null;
            var dir = UcaTool.ReadIndex (file, 0x10, count, "image");
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    internal static class UcaTool
    {
        public static List<Entry> ReadIndex (ArcView file, uint index_offset, int count, string entry_type)
        {
            uint data_offset = index_offset + (uint)count * 0x10;
            uint next_offset = file.View.ReadUInt32 (index_offset+0xC);
            if (next_offset < data_offset)
                return null;
            var invalid_chars = Path.GetInvalidFileNameChars();
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                if (string.IsNullOrWhiteSpace (name) || name.IndexOfAny (invalid_chars) != -1)
                    return null;
                index_offset += 0x10;
                var entry = new Entry { Name = name, Type = entry_type };
                entry.Offset = next_offset;
                if (i+1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset+0xC);
                if (next_offset <= entry.Offset || next_offset > file.MaxOffset)
                    return null;
                entry.Size = (uint)(next_offset - entry.Offset);
                dir.Add (entry);
            }
            return dir;
        }
    }
}
