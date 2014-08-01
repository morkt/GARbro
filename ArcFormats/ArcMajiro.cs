//! \file       ArcMajiro.cs
//! \date       Thu Jul 31 20:27:26 2014
//! \brief      Majiro engine resource archive.
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Majiro
{
    internal class MajiroEntry : Entry
    {
        public bool IsEncrypted { get; set; }
    }

    internal class MajiroArchive : ArcFile
    {
        public uint Key { get; private set; }

        public MajiroArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, uint key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    public class MajiroOptions : ResourceOptions
    {
        public uint Key { get; set; }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string Tag { get { return "MAJIRO"; } }
        public override string Description { get { return "Majiro game engine resource archive"; } }
        public override uint Signature { get { return 0x696a614d; } }
        public override bool IsHierarchic { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version;
            if (file.View.AsciiEqual (0, "MajiroArcV1.000\0"))
                version = 1;
//            else if (file.View.AsciiEqual (0, "MajiroArcV2.000\0"))
//                version = 2;
            else
                return null;
            int count = file.View.ReadInt32 (16);
            uint names_offset = file.View.ReadUInt32 (20);
            uint data_offset = file.View.ReadUInt32 (24);
            if (data_offset <= names_offset || count > 0xfffff || count < 0
                || data_offset >= file.MaxOffset)
                return null;
            int table_size = count + (1 == version ? 1 : 0);
            table_size *= 4 * (version+1);
            if (table_size + 0x1c != names_offset)
                return null;
            if (data_offset != file.View.Reserve (0, data_offset))
                return null;
            int names_size = (int)(data_offset - names_offset);
            var names = new byte[names_size];
            file.View.Read (names_offset, names, 0, (uint)names_size);
            int names_pos = 0;
            uint table_pos = 0x1c;
            uint offset_next = file.View.ReadUInt32 (table_pos+4);

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var zero = Array.IndexOf (names, (byte)0, names_pos, names_size);
                if (-1 == zero)
                    break;
                int name_len = zero-names_pos;
                string name = Encodings.cp932.GetString (names, names_pos, name_len);
                names_size -= name_len+1;
                names_pos = zero+1;
                uint offset = offset_next;
                offset_next = file.View.ReadUInt32 (table_pos + 12);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = offset;
                entry.Size   = offset_next >= offset ? offset_next - offset : 0;
                table_pos += 8;
                if (entry.CheckPlacement (file.MaxOffset))
                    dir.Add (entry);
            }
            if (!dir.Any())
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
