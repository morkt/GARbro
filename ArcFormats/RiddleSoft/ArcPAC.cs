//! \file       ArcPAC.cs
//! \date       Fri Feb 20 13:51:48 2015
//! \brief      PAC archive implementation.
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

using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System;

namespace GameRes.Formats.Riddle
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC1"; } }
        public override string Description { get { return "Riddle Soft resource archive"; } }
        public override uint     Signature { get { return 0x31434150; } } // 'PAC1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 8;
            long base_offset = count*0x20 + 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                uint size = file.View.ReadUInt32 (index_offset+0x10);
                var entry = new PackedEntry { Name = name };
                if (name.EndsWith (".scp", StringComparison.InvariantCultureIgnoreCase))
                {
                    entry.Type = "script";
                    entry.IsPacked = size > 12 && file.View.AsciiEqual (index_offset+0x14, "CMP1");
                }
                else
                {
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                    entry.IsPacked = false;
                }
                if (entry.IsPacked)
                    entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x18);
                else
                    entry.UnpackedSize = size;
                entry.Offset = base_offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                base_offset += size;
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pentry = entry as PackedEntry;
            if (null == pentry || !pentry.IsPacked
                || !arc.File.View.AsciiEqual (entry.Offset, "CMP1"))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            int unpacked_size = arc.File.View.ReadInt32 (entry.Offset+4);
            using (var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12))
            {
                var reader = new CmpReader (input, (int)entry.Size, unpacked_size);
                reader.Unpack();
                return new BinMemoryStream (reader.Data, entry.Name);
            }
        }
    }
}
