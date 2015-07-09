//! \file       ArcCherry.cs
//! \date       Wed Jun 24 21:22:56 2015
//! \brief      Cherry Soft archives implementation.
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

namespace GameRes.Formats.Cherry
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/CHERRY"; } }
        public override string Description { get { return "Cherry Soft resource archive"; } }
        public override uint     Signature { get { return 0x52454843; } } // 'CHER'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
            Signatures = new uint[] { 0x52454843, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int index_offset = 0;
            if (file.View.AsciiEqual (0, "CHERRY PACK 2.0"))
                index_offset = 0x14;
            int count = file.View.ReadInt32 (index_offset);
            if (count <= 0 || count > 0xfffff)
                return null;
            long base_offset = file.View.ReadUInt32 (index_offset+4);
            index_offset += 8;
            uint index_size = (uint)count * 0x18u;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            string arc_name = Path.GetFileNameWithoutExtension (file.Name);
            bool is_grp = arc_name.EndsWith ("GRP", StringComparison.InvariantCultureIgnoreCase);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x10);
                if (0 == name.Length)
                    return null;
                var offset = base_offset + file.View.ReadUInt32 (index_offset+0x10);
                Entry entry;
                if (is_grp)
                {
                    entry = new Entry {
                        Name = Path.ChangeExtension (name, "grp"),
                        Type = "image",
                        Offset = offset
                    };
                }
                else
                {
                    entry = AutoEntry.Create (file, offset, name);
                }
                entry.Size = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
