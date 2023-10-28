//! \file       ArcLIB.cs
//! \date       2023 Oct 15
//! \brief      System-98 resource archive (PC-98).
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.System98
{
    [Export(typeof(ArchiveFormat))]
    public class LibOpener : ArchiveFormat
    {
        public override string         Tag => "LIB/SYSTEM98";
        public override string Description => "System-98 engine resource archive";
        public override uint     Signature => 0x3062694C; // 'Lib0'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            var cat_name = Path.ChangeExtension (file.Name, ".CAT");
            if (!VFS.FileExists (cat_name))
                return null;
            int count;
            byte[] index;
            using (var cat = VFS.OpenView (cat_name))
            {
                count = cat.View.ReadInt16 (4);
                if (!IsSaneCount (count))
                    return null;
                int index_size = count * 0x16;
                if (cat.View.AsciiEqual (0, "Cat0"))
                {
                    index = file.View.ReadBytes (6, (uint)index_size);
                }
                else if (cat.View.AsciiEqual (0, "Cat1"))
                {
                    index = new byte[index_size];
                    using (var input = cat.CreateStream (6))
                        LzssUnpack (input, index);
                }
                else
                    return null;
            }
            int pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, pos, 0xC).TrimEnd();
                var entry = Create<PackedEntry> (name);
                entry.Size   = index.ToUInt32 (pos+0xE);
                entry.Offset = index.ToUInt32 (pos+0x12);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = index[pos+0xC] != 0;
                dir.Add (entry);
                pos += 0x16;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (pent.UnpackedSize == 0)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+6);
            var data = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (entry.Offset+10, entry.Size-10))
            {
                int length = LzssUnpack (input, data);
                return new BinMemoryStream (data, 0, length, entry.Name);
            }
        }

        internal static int LzssUnpack (IBinaryStream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int frame_pos = 1;
            int ctl = 0;
            byte mask = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                mask <<= 1;
                if (0 == mask)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    mask = 1;
                }
                if (input.PeekByte() == -1)
                    break;
                if ((ctl & mask) != 0)
                {
                    output[dst++] = frame[frame_pos++ & 0xFFF] = input.ReadUInt8();
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    int count = (lo & 0xF) + 3;
                    int off = hi << 4 | lo >> 4;
                    while (count --> 0)
                    {
                        byte b = frame[off++ & 0xFFF];
                        output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                    }
                }
            }
            return dst;
        }
    }
}
