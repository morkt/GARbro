//! \file       ArcBlackPackage.cs
//! \date       Wed Apr 15 14:58:52 2015
//! \brief      Black Package archive format.
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

namespace GameRes.Formats.Ffa
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FFA/DAT"; } }
        public override string Description { get { return "FFA System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string lst_name = Path.ChangeExtension (file.Name, ".lst");
            if (lst_name == file.Name || !VFS.FileExists (lst_name))
                return null;
            var lst_entry = VFS.FindFile (lst_name);
            int count = (int)(lst_entry.Size/0x16);
            if (count > 0xffff || count*0x16 != lst_entry.Size)
                return null;
            using (var lst = VFS.OpenView (lst_entry))
            {
                var dir = new List<Entry> (count);
                uint index_offset = 0;
                for (int i = 0; i < count; ++i)
                {
                    string name = lst.View.ReadString (index_offset, 14);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = lst.View.ReadUInt32 (index_offset+14);
                    entry.Size = lst.View.ReadUInt32 (index_offset+18);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x16;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (entry.Size <= 8)
                return input;
            if (!entry.Name.HasAnyOfExtensions ("so4", "so5"))
                return input;
            int packed = input.ReadInt32();
            int unpacked = input.ReadInt32();
            if (packed+8 != entry.Size || packed <= 0 || unpacked <= 0)
            {
                input.Position = 0;
                return input;
            }
            using (input)
            using (var reader = new LzssReader (input, packed, unpacked))
            {
                reader.Unpack();
                return new BinMemoryStream (reader.Data, entry.Name);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class JDatOpener : DatOpener
    {
        public override string         Tag { get { return "FFA/JDAT"; } }
        public override string Description { get { return "FFA System resource archive v2"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public JDatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = file.View.ReadUInt32 (0);
            if (index_offset >= file.MaxOffset)
                return null;
            int index_size = (int)(file.MaxOffset - index_offset);
            int entry_size = 0x34;
            int rem;
            int count = Math.DivRem (index_size, entry_size, out rem);
            if (0 != rem || !IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = 4 + file.View.ReadUInt32 (index_offset+0x20);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
