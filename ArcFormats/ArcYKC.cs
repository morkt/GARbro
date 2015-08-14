//! \file       ArcYKC.cs
//! \date       Thu Aug 13 21:52:01 2015
//! \brief      Yuka engine resource archives.
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Yuka
{
    internal class YukaEntry : Entry
    {
        public uint NameOffset;
        public uint NameLength;
    }

    [Export(typeof(ArchiveFormat))]
    public class YkcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "YKC"; } }
        public override string Description { get { return "Yuka engine resource archive"; } }
        public override uint     Signature { get { return 0x30434B59; } } // 'YKC0'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x3130 != file.View.ReadUInt32 (4))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x10);
            uint index_length = file.View.ReadUInt32 (0x14);
            int count = (int)(index_length / 0x14);
            if (index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            if (index_length > file.View.Reserve (index_offset, index_length))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new YukaEntry();
                entry.NameOffset = file.View.ReadUInt32 (index_offset);
                entry.NameLength = file.View.ReadUInt32 (index_offset+4);
                entry.Offset = file.View.ReadUInt32 (index_offset+8);
                entry.Size   = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x14;
            }
            // read in two cycles to avoid memory mapped file page switching when accessing names
            foreach (var entry in dir.Cast<YukaEntry>())
            {
                entry.Name = file.View.ReadString (entry.NameOffset, entry.NameLength);
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
