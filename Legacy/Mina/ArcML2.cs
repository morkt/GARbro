//! \file       ArcML2.cs
//! \date       2018 Jan 15
//! \brief      Mina resource archive.
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

// [000630][Mina] Lens no Mukougawa...

namespace GameRes.Formats.Mina
{
    [Export(typeof(ArchiveFormat))]
    public class Ml2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ML2"; } }
        public override string Description { get { return "Mina resource archive"; } }
        public override uint     Signature { get { return 0x30324C4D; } } // 'ML200'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            long data_offset = file.View.ReadUInt16 (6);
            uint index_size = file.View.ReadUInt32 (0xC);
            uint index_offset = file.View.ReadUInt32 (0x10);
            if (index_offset >= file.MaxOffset || index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset);
                if (uint.MaxValue == size)
                    break;
                uint name_length = file.View.ReadByte (index_offset+4);
                var name = file.View.ReadString (index_offset+5, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += size;
                index_offset += 5 + name_length;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
