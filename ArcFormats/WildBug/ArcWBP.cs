//! \file       ArcWBP.cs
//! \date       Thu Jul 09 17:02:03 2015
//! \brief      Wild Bug's resource archive format.
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

namespace GameRes.Formats.WildBug
{
    [Export(typeof(ArchiveFormat))]
    public class WbpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WBP"; } }
        public override string Description { get { return "Wild Bug's engine resource archive"; } }
        public override uint     Signature { get { return 0x46435241; } } // 'ARCF'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "ARCFORM") ||
                !file.View.AsciiEqual (8, " WBUG "))
                return null;
            int version = file.View.ReadByte (7) - '0';
            if (version < 2 || version > 4)
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x14);
            uint index_size   = file.View.ReadUInt32 (0x18);
            uint data_offset  = file.View.ReadUInt32 (0x1C);
            if (data_offset < index_offset || data_offset > file.MaxOffset)
                return null;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            if (4 == version)
                return OpenV4 (file, count, index_offset, index_size);
            else
                return OpenV3 (file, count, index_offset);
        }

        ArcFile OpenV3 (ArcView file, int count, uint index_offset)
        {
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (index_offset+9);
                string name = file.View.ReadString (index_offset+0x14, name_length);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += name_length + 0x14;
            }
            return new ArcFile (file, this, dir);
        }

        ArcFile OpenV4 (ArcView file, int count, uint index_offset, uint index_size)
        {
            var buffer = new byte[0x400];
            var dir_names = new uint[0x100];
            file.View.Read (0x24, buffer, 0, 0x400);
            Buffer.BlockCopy (buffer, 0, dir_names, 0, 0x400);
            var res_names = new uint[0x100];
            file.View.Read (0x424, buffer, 0, 0x400);
            Buffer.BlockCopy (buffer, 0, res_names, 0, 0x400);

            var dir_table = new Dictionary<int, string>();
            for (int i = 0; i < 0x100; ++i)
            {
                if (0 == dir_names[i])
                    continue;
                uint dir_offset = dir_names[i];
                for (;;)
                {
                    byte hash = file.View.ReadByte (dir_offset);
                    if (hash != i)
                        break;
                    byte name_length = file.View.ReadByte (dir_offset+1);
                    int dir_id = file.View.ReadUInt16 (dir_offset+2);
                    file.View.Read (dir_offset + 4, buffer, 0, name_length);
                    byte checksum = 0;
                    for (int j = 0; j < name_length; ++j)
                        checksum += buffer[j];
                    if (hash != checksum)
                        return null;
                    dir_table[dir_id] = Encodings.cp932.GetString (buffer, 0, name_length).TrimStart ('\\');
                    dir_offset += 5u + name_length;
                }
            }
            uint index_end = index_offset + index_size;
            var dir = new List<Entry> (count);
            for (int i = 0; i < 0x100; ++i)
            {
                if (0 == res_names[i])
                    continue;
                uint res_offset = res_names[i];
                while (res_offset < index_end)
                {
                    byte hash = file.View.ReadByte (res_offset);
                    if (hash != i)
                        break;
                    byte name_length = file.View.ReadByte (res_offset+1);
                    int dir_id  = file.View.ReadUInt16 (res_offset+2);
                    file.View.Read (res_offset+0x14, buffer, 0, name_length);
                    byte checksum = 0;
                    for (int j = 0; j < name_length; ++j)
                        checksum += buffer[j];
                    if (hash != checksum)
                        return null;
                    var dir_name = dir_table[dir_id];
                    var name = dir_table[dir_id] + Encodings.cp932.GetString (buffer, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (res_offset+4);
                    entry.Size   = file.View.ReadUInt32 (res_offset+8);
                    res_offset += (0x18u + name_length) & ~3u;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }
    }
}
