//! \file       ArcWSM.cs
//! \date       Sun Jan 29 05:30:28 2017
//! \brief      Music archive format by Tanaka Tatsuhiro.
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
using GameRes.Utility;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class WsmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WSM"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine music archive"; } }
        public override uint     Signature { get { return 0x324D5357; } } // 'WSM2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count) || index_size >= file.MaxOffset - 0x40)
                return null;
            int table_offset = file.View.ReadInt32 (0x10);
            int table_count = file.View.ReadInt32 (0x14);
            if (table_offset >= index_size || !IsSaneCount (table_count))
                return null;
            var index = file.View.ReadBytes (0x40, index_size);
            var dir = new List<Entry> (count);
            for (int i = 0; i < table_count; ++i)
            {
                var entry = new Entry { Type = "audio" };
                entry.Offset = index.ToUInt32 (table_offset) - 0x14;
                entry.Size   = index.ToUInt32 (table_offset+8) + 0x14;
                table_offset += 0x20;
                dir.Add (entry);
            }
            int index_offset = 0;
            for (int i = 0; i < count; ++i)
            {
                int entry_pos = index.ToInt32 (index_offset);
                index_offset += 4;
                int name_length = index[entry_pos+1];
                var name = Binary.GetCString (index, entry_pos+2, name_length-2);
                if (0 == name.Length)
                    return null;
                entry_pos += name_length;
                int entry_idx = index[entry_pos+3];
                if (entry_idx >= dir.Count)
                    return null;
                var entry = dir[entry_idx];
                entry.Name = string.Format ("{0:D2}_{1}.wav", entry_idx, name);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
