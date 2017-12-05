//! \file       ArcBIN.cs
//! \date       2017 Dec 03
//! \brief      Rain Software resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Text.RegularExpressions;
using GameRes.Compression;

namespace GameRes.Formats.Rain
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/RAIN"; } }
        public override string Description { get { return "Rain Software resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex PackNameRe = new Regex (@"^pack(...)\.bin$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            bool is_compressed = 0 == (count & 0x80000000);
            count &= 0x7FFFFFFF;
            if (!IsSaneCount (count))
                return null;
            var match = PackNameRe.Match (Path.GetFileName (file.Name));
            if (!match.Success)
                return null;
            var ext = match.Groups[1].Value;
            uint index_size = (uint)count * 12;
            if (index_size > file.View.Reserve (4, index_size))
                return null;
            uint index_offset = 4;
            uint data_offset = 4 + index_size;
            var dir = new List<Entry> (count);
            var seen_nums = new HashSet<uint>();
            for (int i = 0; i < count; ++i)
            {
                uint num = file.View.ReadUInt32 (index_offset);
                if (num > 0xFFFFFF || !seen_nums.Add (num))
                    return null;
                var name = string.Format ("{0:D5}.{1}", num, ext);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "SZDD"))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12);
            var lzss = new LzssStream (input);
            lzss.Config.FrameFill = 0x20;
            lzss.Config.FrameInitPos = 0xFF0;
            return lzss;
        }
    }
}
