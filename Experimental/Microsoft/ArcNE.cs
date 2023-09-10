//! \file       ArcNE.cs
//! \date       2023 Aug 29
//! \brief      Access 16-bit NE executable resources.
//
// Copyright (C) 2023 by morkt
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

namespace GameRes.Formats.Microsoft
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get => "EXE/NE"; }
        public override string Description { get => "Windows 16-bit executable resources"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => true; }
        public override bool      CanWrite { get => false; }

        static readonly Dictionary<int, string> TypeMap = new Dictionary<int, string> {
            { 1, "RT_CURSOR" },
            { 2, "RT_BITMAP" },
            { 3, "RT_ICON" },
            { 4, "RT_MENU" },
            { 5, "RT_DIALOG" },
            { 6, "RT_STRING" },
            { 10, "RT_DATA" },
            { 11, "RT_MESSAGETABLE" },
            { 16, "RT_VERSION" },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "MZ"))
                return null;
            uint ne_offset = file.View.ReadUInt32 (0x3C);
            if (!file.View.AsciiEqual (ne_offset, "NE"))
                return null;
            uint res_table_offset = file.View.ReadUInt16 (ne_offset+0x24) + ne_offset;
            if (res_table_offset <= ne_offset || res_table_offset >= file.MaxOffset)
                return null;
            int shift = file.View.ReadUInt16 (res_table_offset);
            res_table_offset += 2;
            var dir = new List<Entry>();
            while (res_table_offset + 1 < file.MaxOffset)
            {
                int type_id = file.View.ReadUInt16 (res_table_offset);
                if (0 == type_id)
                    break;
                string dir_name = null;
                if ((type_id & 0x8000) != 0)
                {
                    type_id &= 0x7FFF;
                    TypeMap.TryGetValue (type_id, out dir_name);
                }
                int count = file.View.ReadUInt16 (res_table_offset+2);
                res_table_offset += 8;
                if (null == dir_name)
                    dir_name = string.Format ("#{0}", type_id);
                for (int i = 0; i < count; ++i)
                {
                    int offset = file.View.ReadUInt16 (res_table_offset) << shift;
                    uint size = (uint)file.View.ReadUInt16 (res_table_offset+2) << shift;
                    int res_id = file.View.ReadUInt16 (res_table_offset+6);
                    res_table_offset += 12;
                    string name = res_id.ToString ("D5");
                    name = string.Join ("/", dir_name, name);
                    var entry = new Entry {
                        Name = name,
                        Offset = offset,
                        Size = size,
                    };
                    dir.Add (entry);
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
