//! \file       ArcWIP.cs
//! \date       Thu Jul 07 07:34:00 2016
//! \brief      Will multi-frame image implementation.
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
using GameRes.Utility;

namespace GameRes.Formats.Will
{
    internal class WipfEntry : PackedEntry
    {
        public override string Type { get { return "image"; } }

        public byte[]   Header;
    }

    [Export(typeof(ArchiveFormat))]
    public class WipOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WIP/MULTI"; } }
        public override string Description { get { return "Will Co. multi-frame image format"; } }
        public override uint     Signature { get { return 0x46504957u; } } // 'WIPF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public WipOpener ()
        {
            Extensions = new string[] { "wip" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var wipf_header = file.View.ReadBytes (0, 0x20);
            int bpp = LittleEndian.ToInt16 (wipf_header, 6);
            LittleEndian.Pack ((short)1, wipf_header, 4);
            int index_offset = 8;
            long entry_offset = 8 + 0x18 * count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new WipfEntry { Name = string.Format ("{0}#{1:D4}.wip", base_name, i) };
                entry.Size = file.View.ReadUInt32 (index_offset+0x14);
                entry.Offset = entry_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Header = wipf_header.Clone() as byte[];
                file.View.Read (index_offset, entry.Header, 8, 0x18);
                if (8 == bpp)
                    entry.Size += 0x400;
                entry.IsPacked = true;
                entry.UnpackedSize = entry.Size + (uint)entry.Header.Length;
                dir.Add (entry);
                index_offset += 0x18;
                entry_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = (WipfEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (went.Header, input);
        }
    }
}
