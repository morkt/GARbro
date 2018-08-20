//! \file       ArcVBD.cs
//! \date       2018 Aug 18
//! \brief      Witch audio archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Witch
{
    [Export(typeof(ArchiveFormat))]
    public class SoundDataOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VBD/SOUND"; } }
        public override string Description { get { return "Witch audio archive"; } }
        public override uint     Signature { get { return 0x4E554F53; } } // 'SOUNDDATE'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SOUNDDATE "))
                return null;
            int count = file.View.ReadInt32 (0xA);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 0xE;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[0x100];
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (offset <= index_offset || offset > file.MaxOffset)
                    return null;
                int name_length = file.View.ReadInt32 (index_offset+4);
                if (name_length <= 0)
                    return null;
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                var name = file.View.ReadString (index_offset+8, (uint)name_length);
                var entry = new Entry {
                    Name = name,
                    Type = "audio",
                    Offset = offset,
                };
                dir.Add (entry);
                index_offset += 8 + name_length;
            }
            ImageDataOpener.SetAdjacentEntriesSize (dir, file.MaxOffset);
            return new ArcFile (file, this, dir);
        }
    }
}
