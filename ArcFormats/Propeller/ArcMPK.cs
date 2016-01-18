//! \file       ArcMPK.cs
//! \date       Sat Nov 21 00:56:44 2015
//! \brief      Propeller resource archive.
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

namespace GameRes.Formats.Propeller
{
    [Export(typeof(ArchiveFormat))]
    public class MpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MPK"; } }
        public override string Description { get { return "Propeller resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (0);
            int count = file.View.ReadInt32 (4);
            if (index_offset < 8 || index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            uint index_size = (uint)count * 0x28u;
            if (index_size > file.MaxOffset - index_offset)
                return null;
            var index = file.View.ReadBytes (index_offset, index_size);
            // last byte of the first filename presumably is zero
            byte key = index[0x1F];
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key;

            int current = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_offset = '\\' == index[current] ? 1 : 0;
                var name = Binary.GetCString (index, current+name_offset, 0x20-name_offset);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, current+0x20);
                entry.Size   = LittleEndian.ToUInt32 (index, current+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith (".msc", StringComparison.InvariantCultureIgnoreCase))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
                data[i] ^= 0x88;
            return new MemoryStream (data);
        }
    }
}
