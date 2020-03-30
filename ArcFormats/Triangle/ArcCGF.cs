//! \file       ArcCGF.cs
//! \date       Tue Feb 02 00:47:55 2016
//! \brief      route2 engine CG archive.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Triangle
{
    internal class CgfEntry : Entry
    {
        public uint Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class CgfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CGF"; } }
        public override string Description { get { return "route2 engine CG archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || file.MaxOffset >= ~0xC0000000)
                return null;
            uint offset1 = file.View.ReadUInt32 (0x14);
            uint offset2 = file.View.ReadUInt32 (0x20);
            uint entry_size, next_offset;
            if (4+(uint)count*0x14 == (offset1 & ~0xC0000000))
            {
                entry_size = 0x14;
                next_offset = offset1;
            }
            else if (4+(uint)count*0x20 == (offset2 & ~0xC0000000))
            {
                entry_size = 0x20;
                next_offset = offset2;
            }
            else
                return null;

            uint index_size = entry_size * (uint)count;
            if (index_size > file.View.Reserve (4, index_size))
                return null;

            uint index_offset = 4;
            uint size = file.View.ReadUInt32 (index_offset + entry_size - 8);
            offset2 = file.View.ReadUInt32 (index_offset + (entry_size * 2) - 4);
            if (size == (offset2 - next_offset)) // route2 archives shouldn't have entry size
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, entry_size-4);
                if (!IsValidEntryName (name))
                    return null;
                uint flags = next_offset >> 30;
                Entry entry;
                if (1 == flags || name.HasExtension (".iaf"))
                    entry = new Entry();
                else
                    entry = new CgfEntry { Flags = flags };
                entry.Name = name;
                entry.Type = "image";
                entry.Offset = next_offset & ~0xC0000000;

                index_offset += entry_size;
                next_offset = i+1 == count ? (uint)file.MaxOffset : file.View.ReadUInt32 (index_offset+entry_size-4);
                if (next_offset < entry.Offset)
                    return null;
                entry.Size = (next_offset & ~0xC0000000) - (uint)entry.Offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var cent = entry as CgfEntry;
            if (null == cent)
                return base.OpenEntry (arc, entry);
            var offset = entry.Offset;
            var header = new byte[12];
            if (2 == cent.Flags)
            {
                arc.File.View.Read (offset, header, 0, 8);
                offset += 0x10;
            }
            uint packed_size = arc.File.View.ReadUInt32 (offset);
            arc.File.View.Read (offset+4, header, 8, 4);
            var input = arc.File.CreateStream (offset+8, packed_size);
            return new PrefixStream (header, input);
        }
    }
}
