//! \file       ArcBMX.cs
//! \date       Sat Jan 28 17:16:32 2017
//! \brief      Archive format by Tanaka Tatsuhiro.
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

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class BmxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BMX"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint total_size = file.View.ReadUInt32 (0);
            if (total_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint index_offset = 0x10;
            uint next_offset = file.View.ReadUInt32 (index_offset+0x1C);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x1C);
                if (0 == name.Length)
                    break;
                index_offset += 0x20;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = next_offset;
                if (i+1 < count)
                {
                    next_offset = file.View.ReadUInt32 (index_offset+0x1C);
                    if (0 == next_offset)
                        next_offset = (uint)file.MaxOffset;
                }
                else
                {
                    next_offset = (uint)file.MaxOffset;
                }
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (string.IsNullOrEmpty (entry.Type))
                    entry.Type = "image";
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
