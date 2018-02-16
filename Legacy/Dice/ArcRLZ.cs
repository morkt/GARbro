//! \file       ArcRLZ.cs
//! \date       2017 Dec 11
//! \brief      DiceSystem resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000623][Marimo] Setsunai

namespace GameRes.Formats.Dice
{
    [Export(typeof(ArchiveFormat))]
    public class RlzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "RLZ"; } }
        public override string Description { get { return "DiceSystem resource archive"; } }
        public override uint     Signature { get { return 0x325A4C52; } } // 'RLZ2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            long data_offset = index_offset + count * 0x34;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Size = file.View.ReadUInt32 (index_offset+0x20);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x24);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x2C) + data_offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = file.View.ReadInt32 (index_offset+0x28) != 0;
                dir.Add (entry);
                index_offset += 0x34;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            using (input)
            {
                var output = new byte[pent.UnpackedSize];
                LzUnpack (input, output);
                return new BinMemoryStream (output, entry.Name);
            }
        }

        void LzUnpack (IBinaryStream input, byte[] output)
        {
            var frame = new byte[0x800];
            int frame_pos = 0x7EF;
            int dst = 0;
            int ctl_bits = 2;
            while (dst < output.Length)
            {
                ctl_bits >>= 1;
                if (1 == ctl_bits)
                {
                    ctl_bits = input.ReadByte();
                    if (-1 == ctl_bits)
                        break;
                    ctl_bits |= 0x100;
                }
                if (0 != (ctl_bits & 1))
                {
                    byte v = input.ReadUInt8();
                    output[dst++] = v;
                    frame[frame_pos++ & 0x7FF] = v;
                }
                else
                {
                    byte lo = input.ReadUInt8();
                    byte hi = input.ReadUInt8();
                    int offset = (hi & 0xF0) << 4 | lo;
                    int count = (hi & 0xF) + 2;
                    for (int i = 0; i < count; ++i)
                    {
                        byte v = frame[(offset + i) & 0x7FF];
                        output[dst++] = v;
                        frame[frame_pos++ & 0x7FF] = v;
                    }
                }
            }
        }
    }
}
