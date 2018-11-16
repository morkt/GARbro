//! \file       ArcVCT.cs
//! \date       2018 Feb 17
//! \brief      Unison Shift resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

// [000225][Unison Shift] Moe ~Moegiiro no Machi~

namespace GameRes.Formats.Unison
{
    [Export(typeof(ArchiveFormat))]
    public class VctOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VCT"; } }
        public override string Description { get { return "Unison Shift resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int idx_count = file.View.ReadByte (0);
            if (0 == idx_count)
                return null;
            int index_offset = 1 + idx_count * 3;
            int count = file.View.ReadInt32 (index_offset);
            if (!IsSaneCount (count))
                return null;
            index_offset += 4;
            uint index_size = (uint)count * 0x20;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x14).TrimEnd();
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                string ext  = file.View.ReadString (index_offset+0x14, 3);
                if (!string.IsNullOrWhiteSpace (ext))
                    name += '.' + ext;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x18);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "LZS\0"))
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var data = LzsUnpack (input);
                if (data.AsciiEqual ("BM") && 32 == data.ToUInt16 (0x1C))
                    FixBitmapAlpha (data);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        byte[] LzsUnpack (IBinaryStream input)
        {
            input.ReadInt32();
            int unpacked_size = input.ReadInt32();
            int packed_size = input.ReadInt32();
            int ctl_size = input.ReadInt32();
            var ctl = input.ReadBytes (ctl_size);
            var output = new byte[unpacked_size];
            var frame = new byte[0x1000];
            int frame_pos = 1;
            int src = 0;
            int dst = 0;
            int bits = 2;
            while (dst < unpacked_size)
            {
                bits >>= 1;
                if (1 == bits)
                {
                    bits = ctl[src++] | 0x100;
                }
                if (0 != (bits & 1))
                {
                    output[dst++] = frame[frame_pos++ & 0xFFF] = input.ReadUInt8();
                }
                else
                {
                    int offset = input.ReadUInt16();
                    int count = (offset >> 12) + 2;
                    while (count --> 0)
                    {
                        byte b = frame[offset++ & 0xFFF];
                        output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                    }
                }
            }
            return output;
        }

        void FixBitmapAlpha (byte[] bmp)
        {
            int img_start = bmp.ToInt32 (0xA);
            for (int pos = img_start; pos < bmp.Length; pos += 4)
            {
                byte r = bmp[pos];
                bmp[pos] = bmp[pos+2];
                bmp[pos+2] = r;
                bmp[pos+3] ^= 0xFF;
            }
            if (img_start == 0x42 && bmp.ToInt32 (0x36) == 0xFF && bmp.ToInt32 (0x3E) == 0xFF0000)
            {
                LittleEndian.Pack (0xFF0000, bmp, 0x36);
                LittleEndian.Pack (0x0000FF, bmp, 0x3E);
            }
        }
    }
}
