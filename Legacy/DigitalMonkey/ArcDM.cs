//! \file       ArcDM.cs
//! \date       2018 Oct 07
//! \brief      Digital Monkey resource archive.
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

// [030725][Digital Monkey] Kono Sora ga Tsuieru Toki ni
// [041112][Yumesta] Seikon ~Kiba to Nie to Kyouki no Yakata~

namespace GameRes.Formats.DigitalMonkey
{
    [Export(typeof(ArchiveFormat))]
    public class DmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DM"; } }
        public override string Description { get { return "Digital Monkey resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dm"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            string type = arc_name == "image" ? "image"
                        : arc_name == "sound" ? "audio" : "";
            uint index_offset = 4;
            uint data_offset = 4 + (uint)count * 0x2C;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = new PackedEntry {
                    Name = name,
                    Type = type,
                    UnpackedSize = file.View.ReadUInt32 (index_offset+0x20),
                    Size   = file.View.ReadUInt32 (index_offset+0x24),
                    Offset = file.View.ReadUInt32 (index_offset+0x28),
                    IsPacked = true,
                };
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
