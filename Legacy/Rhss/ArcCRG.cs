//! \file       ArcCRG.cs
//! \date       2023 Aug 28
//! \brief      Raishū Hyōjun Script System resource archive.
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

using GameRes.Compression;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Rhss
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "DAT/CRG";
        public override string Description => "RHSS engine resource archive";
        public override uint     Signature => 0x00475243; // 'CRG'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_pos = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_pos+8, 0x30);
                var entry = Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos);
                entry.Size   = file.View.ReadUInt32 (index_pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 60;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null != pent)
            {
                if (!pent.IsPacked && arc.File.View.AsciiEqual (entry.Offset, "CMP\0"))
                {
                    pent.IsPacked = true;
                    pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset + 0x4C);
                }
                if (pent.IsPacked)
                {
                    Stream input = arc.File.CreateStream (entry.Offset+0x50, entry.Size-0x50);
                    input = new ZLibStream (input, CompressionMode.Decompress);
                    return new XoredStream (input, 0xFF);
                }
            }
            return base.OpenEntry (arc, entry);
        }
    }
}
