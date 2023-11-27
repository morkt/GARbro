//! \file       ArcBND.cs
//! \date       2023 Sep 28
//! \brief      Ponytail Adventure System resource archive.
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
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;

// [951115][Ponytail Soft] Masuzume Yume

namespace GameRes.Formats.Ponytail
{
    [Export(typeof(ArchiveFormat))]
    public class BndOpener : ArchiveFormat
    {
        public override string         Tag => "BND/NMI";
        public override string Description => "Ponytail Soft resource archive";
        public override uint     Signature => 0x646E6942; // 'Bind'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, " ver.0"))
                return null;
            int count = file.View.ReadInt16 (0xD);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xF);
            if (index_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 8).Trim();
                var ext  = file.View.ReadString (index_offset+8, 3);
                name = name + '.' + ext;
                var entry = Create<PackedEntry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x0C);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            foreach (PackedEntry entry in dir.Where (e => e.Name.EndsWith ("Z") && e.Type != "image"))
            {
                if (file.View.AsciiEqual (entry.Offset, "lz1_"))
                {
                    entry.IsPacked = true;
                    char last_chr =(char)file.View.ReadByte (entry.Offset+4);
                    entry.UnpackedSize = file.View.ReadUInt32 (entry.Offset+5);
                    string name = entry.Name.Remove (entry.Name.Length-1);
                    entry.Name = name + char.ToUpperInvariant (last_chr);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            if (!pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var output = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (pent.Offset+9, pent.Size-9))
                Lz1Unpack (input, output);
            return new BinMemoryStream (output, pent.Name);
        }

        internal static void Lz1Unpack (IBinaryStream input, byte[] output)
        {
            byte mask = 0;
            int ctl = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                mask <<= 1;
                if (0 == mask)
                {
                    ctl = input.ReadUInt8();
                    if (ctl < 0)
                        break;
                    mask = 1;
                }
                if ((ctl & mask) != 0)
                {
                    output[dst++] = input.ReadUInt8();
                }
                else
                {
                    int code = input.ReadUInt16();
                    int offset = (code >> 5) + 1;
                    int count = Math.Min (3 + (code & 0x1F), output.Length - dst);
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
            }
        }
    }
}
