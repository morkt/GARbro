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
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string lst_name = Path.ChangeExtension (file.Name, ".lst");
            var lst_info = new FileInfo (lst_name);
            if (!lst_info.Exists)
                return null;
            int count = (int)(lst_info.Length/0x16);
            if (count > 0xffff || count*0x16 != lst_info.Length)
                return null;
            using (var lst = new ArcView (lst_name))
            {
                var dir = new List<Entry> (count);
                uint index_offset = 0;
                for (int i = 0; i < count; ++i)
                {
                    string name = lst.View.ReadString (index_offset, 14);
                    var entry = FormatCatalog.Instance.CreateEntry (name);
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
            if (entry.Size <= 8 || !entry.Name.EndsWith (".so4", StringComparison.InvariantCultureIgnoreCase))
                return input;
            using (var header = new ArcView.Reader (input))
            {
                int packed = header.ReadInt32();
                int unpacked = header.ReadInt32();
                if (packed+8 != entry.Size || packed <= 0 || unpacked <= 0)
                    return input;
                using (input)
                using (var reader = new LzssReader (input, packed, unpacked))
                {
                    reader.Unpack();
                    return new MemoryStream (reader.Data);
                }
            }
        }
    }
}
