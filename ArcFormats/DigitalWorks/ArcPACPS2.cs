//! \file       ArcPACPS2.cs
//! \date       2018 Sep 18
//! \brief      Digital Works PS2 resource archive.
//
// Copyright (C) 2018 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.DigitalWorks
{
    [Export(typeof(ArchiveFormat))]
    public class PacPS2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/PS2"; } }
        public override string Description { get { return "Digital Works PS2 resource archive"; } }
        public override uint     Signature { get { return 0x434150; } } // 'PAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        /**
         Target games:
        Cafe Little Wish SLPM-65294
        F Fanatic SLPM-65296
         */

        public override ArcFile TryOpen (ArcView file)
        {
            uint filename_index = file.View.ReadUInt32(4);
            int count = file.View.ReadInt32(8);
            uint index_offset = 0x0C;
            
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            int i = 0;
            while (i < count)
            {
                var name = file.View.ReadString (filename_index + i * 0x40, 0x40);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset + i * 8);
                entry.Size   = file.View.ReadUInt32 (index_offset + i * 8 + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                i++;
            }
            return new ArcFile (file, this, dir);    
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                if (!arc.File.View.AsciiEqual (entry.Offset, "LZS\0"))
                    return base.OpenEntry (arc, entry);
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            bool embedded_lzs = (input.Signature & ~0xF0u) == 0x535A4C0F; // 'LZS'
            var lzs = new LzssStream (input);
            lzs.Config.FrameFill = 0x20;
            if (embedded_lzs)
            {
                var header = new byte[8];
                lzs.Read (header, 0, 8);
                pent.UnpackedSize = header.ToUInt32 (4);
                lzs = new LzssStream (lzs);
                lzs.Config.FrameFill = 0x20;
            }
            return lzs;
        }
    }
}
