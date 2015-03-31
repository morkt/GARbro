//! \file       ArcMBL.cs
//! \date       Fri Mar 27 23:11:19 2015
//! \brief      Marble Engine archive implementation.
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

namespace GameRes.Formats.Marble
{
    [Export(typeof(ArchiveFormat))]
    public class MblOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MBL"; } }
        public override string Description { get { return "Marble engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfffff)
                return null;
            ArcFile arc = null;
            uint filename_len = file.View.ReadUInt32 (4);
            if (filename_len > 0 && filename_len <= 0xff)
                arc = ReadIndex (file, count, filename_len, 8);
            if (null == arc)
                arc = ReadIndex (file, count, 0x10, 4);
            return arc;
        }

        private ArcFile ReadIndex (ArcView file, int count, uint filename_len, uint index_offset)
        {
            uint index_size = (8u + filename_len) * (uint)count;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, filename_len);
                if (name.Length+1 < filename_len)
                {
                    uint ext_length = (uint)Math.Min (filename_len-name.Length-1, 3);
                    string ext = file.View.ReadString (index_offset+name.Length+1, ext_length);
                    if (!string.IsNullOrEmpty (ext))
                    {
                        if ("OG" == ext || "O" == ext)
                            ext = "OGG";
                        else if ("PR" == ext || "P" == ext)
                            ext = "PRS";
                        else if ("WA" == ext || "W" == ext)
                            ext = "WAY";
                        name = Path.ChangeExtension (name, ext);
                    }
                }
                name = name.ToLowerInvariant();
                var entry = FormatCatalog.Instance.CreateEntry (name);
                index_offset += (uint)filename_len;
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
