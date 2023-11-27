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
using System.Text;

namespace GameRes.Formats.Microsoft
{
    [Export(typeof(ArchiveFormat))]
    [ExportMetadata("Priority", -2)]
    public class NeExeOpener : ArchiveFormat
    {
        public override string         Tag => "EXE/NE";
        public override string Description => "Windows 16-bit executable resources";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

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
            if (!file.View.AsciiEqual (0, "MZ") || file.MaxOffset < 0x40)
                return null;
            uint ne_offset = file.View.ReadUInt32 (0x3C);
            if (ne_offset > file.MaxOffset-2 || !file.View.AsciiEqual (ne_offset, "NE"))
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
                    if ((res_id & 0x8000) != 0)
                        res_id &= 0x7FFF;
                    res_table_offset += 12;
                    string name = res_id.ToString ("D5");
                    name = string.Join ("/", dir_name, name);
                    var entry = new NeResourceEntry {
                        Name = name,
                        Offset = offset,
                        Size = size,
                        NativeName = res_id,
                        NativeType = type_id,
                    };
                    dir.Add (entry);
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rent = (NeResourceEntry)entry;
            if (rent.NativeType == 16)
                return OpenVersion (arc, rent);
            return base.OpenEntry (arc, entry);
        }

        Encoding DefaultEncoding = Encodings.cp932;

        Stream OpenVersion (ArcFile arc, NeResourceEntry entry)
        {
            uint data_length = arc.File.View.ReadUInt16 (entry.Offset);
            var input = arc.File.CreateStream (entry.Offset, data_length);
            for (;;)
            {
                input.ReadUInt16();
                int value_length = input.ReadUInt16();
                if (0 == value_length)
                    break;
                if (input.ReadCString (DefaultEncoding) != "VS_VERSION_INFO")
                    break;
                long pos = (input.Position + 3) & -4L;
                input.Position = pos;
                if (input.ReadUInt32() != 0xFEEF04BDu)
                    break;
                input.Position = pos + value_length;
                int str_info_length = input.ReadUInt16();
                value_length = input.ReadUInt16();
                if (value_length != 0)
                    break;
                if (input.ReadCString (DefaultEncoding) != "StringFileInfo")
                    break;
                pos = (input.Position + 3) & -4L;
                input.Position = pos;
                int info_length = input.ReadUInt16();
                long end_pos = pos + info_length;
                value_length = input.ReadUInt16();
                if (value_length != 0)
                    break;
                var output = new MemoryStream();
                using (var text = new StreamWriter (output, DefaultEncoding, 512, true))
                {
                    string block_name = input.ReadCString (DefaultEncoding);
                    text.WriteLine ("BLOCK \"{0}\"\n{{", block_name);
                    long next_pos = (input.Position + 3) & -4L;
                    while (next_pos < end_pos)
                    {
                        input.Position = next_pos;
                        info_length = input.ReadUInt16();
                        value_length = input.ReadUInt16();
                        next_pos = (next_pos + info_length + 3) & -4L;
                        string key = input.ReadCString (DefaultEncoding);
                        input.Position = (input.Position + 3) & -4L;
                        string value = value_length != 0 ? input.ReadCString (value_length, DefaultEncoding)
                                                         : String.Empty;
                        text.WriteLine ("\tVALUE \"{0}\", \"{1}\"", key, value);
                    }
                    text.WriteLine ("}");
                }
                input.Dispose();
                output.Position = 0;
                return output;
            }
            input.Position = 0;
            return input;
        }
    }

    internal class NeResourceEntry : Entry
    {
        public int NativeType;
        public int NativeName;
    }
}
