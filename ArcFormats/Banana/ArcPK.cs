//! \file       ArcPK.cs
//! \date       Sat Nov 28 02:45:36 2015
//! \brief      BANANA Shu-Shu PK archives implementation.
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
using GameRes.Compression;

namespace GameRes.Formats.Banana // namespace is arbitrary, actual format source is uncertain
{
    [Export(typeof(ArchiveFormat))]
    public class PkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PK/BANANA"; } }
        public override string Description { get { return "BANANA Shu-Shu resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PkOpener ()
        {
            Extensions = new string[] { "pk", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || count * 10 >= file.MaxOffset)
                return null;

            uint index_offset = 4;
            byte[] name_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                byte name_length = file.View.ReadByte (index_offset++);
                if (0 == name_length)
                    return null;
                if (name_length != file.View.Read (index_offset, name_buffer, 0, name_length))
                    return null;
                index_offset += name_length;
                byte key = (byte)(name_length+1);
                for (int j = 0; j < name_length; ++j)
                {
                    name_buffer[j] -= key--;
                    if (name_buffer[j] < 0x20 || name_buffer[j] >= 0xFD)
                        return null;
                }
                string name = Encodings.cp932.GetString (name_buffer, 0, name_length);

                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = Binary.BigEndian (file.View.ReadUInt32 (index_offset));
                entry.Size   = Binary.BigEndian (file.View.ReadUInt32 (index_offset+4));
                index_offset += 8;
                if (entry.Offset < index_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".scr"))
                    entry.IsPacked = true;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LzssStream (input);
        }
    }
}
