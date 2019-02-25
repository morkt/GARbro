//! \file       ArcDAT.cs
//! \date       2018 Apr 12
//! \brief      Studio Sakura resource archive.
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
using GameRes.Compression;

namespace GameRes.Formats.StudioSakura
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/SAKURA"; } }
        public override string Description { get { return "Studio Sakura resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x20;
            long data_offset  = index_offset + count * 0x110;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = new PackedEntry();
                entry.IsPacked = name.HasExtension (".pr3");
                if (entry.IsPacked)
                    name = name.Substring (0, name.Length-4);
                entry.Name   = name;
                entry.Type   = FormatCatalog.Instance.GetTypeFromName (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x100);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x104);
                entry.UnpackedSize = entry.Size;
                if (entry.Offset < data_offset || entry.Offset > file.MaxOffset)
                    return null;
                dir.Add (entry);
                index_offset += 0x110;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !arc.File.View.AsciiEqual (entry.Offset, "ACMPRS03"))
                return base.OpenEntry (arc, entry);
            pent.Size = arc.File.View.ReadUInt32 (entry.Offset+0x14);
            var input = arc.File.CreateStream (entry.Offset+0x24, entry.Size);
            return new LzssStream (input);
        }
    }
}
