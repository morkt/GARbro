//! \file       ArcCircus.cs
//! \date       Mon Jun 15 16:11:56 2015
//! \brief      Circus archive format.
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

namespace GameRes.Formats.Circus
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CIRCUS/DAT"; } }
        public override string Description { get { return "Circus resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfffff)
                return null;
            var dir = ReadIndex (file, count, 0x30);
            if (null == dir)
                return null;
            return new ArcFile (file, this, dir);
        }

        private List<Entry> ReadIndex (ArcView file, int count, int name_length)
        {
            long index_offset = 4;
            uint index_size = (uint)((name_length + 4) * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            --count;
            uint next_offset = file.View.ReadUInt32 (index_offset+name_length);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (0 == file.View.ReadByte (index_offset))
                    return null;
                string name = file.View.ReadString (index_offset, (uint)name_length);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                index_offset += name_length;
                uint offset = next_offset;
                if (i+1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset+4+name_length);
                if (next_offset < offset)
                    return null;
                entry.Size = next_offset - offset;
                entry.Offset = offset;
                if (offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 4;
            }
            return dir;
        }
    }
}
