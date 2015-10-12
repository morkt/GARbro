//! \file       ArcARC.cs
//! \date       Mon Oct 12 12:19:46 2015
//! \brief      Silky's ARC archive implementation.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/SILKY'S"; } }
        public override string Description { get { return "Silky's resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 4;
            const int name_length = 0x20;
            uint index_size = (uint)(count * (name_length + 8));
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, (uint)name_length);
                if (0 == name.Length)
                    return null;
                index_offset += name_length;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < index_size+4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
