//! \file       ArcSKA.cs
//! \date       2022 Jun 12
//! \brief      Sohfu resource archive.
//
// Copyright (C) 2022 by morkt
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

namespace GameRes.Formats.Sohfu
{
    [Export(typeof(ArchiveFormat))]
    public class SkaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SKA/SOHFU"; } }
        public override string Description { get { return "Sohfu resource archive"; } }
        public override uint     Signature { get { return 0x32465049; } } // 'IPF2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_pos = 8;
            var name_buffer = new byte[0x10];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_pos, name_buffer, 0, 0x10);
                int name_len = 0;
                while (name_len < name_buffer.Length && name_buffer[name_len] != 0)
                    ++name_len;
                var name = Encodings.cp932.GetString (name_buffer, 0, name_len);
                ++name_len;
                string ext = null;
                if (name_len < 0x10)
                    ext  = Binary.GetCString (name_buffer, name_len, 0x10 - name_len);
                if (!string.IsNullOrEmpty (ext))
                    name = Path.ChangeExtension (name, ext);
                var entry = Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos+0x10);
                entry.Size   = file.View.ReadUInt32 (index_pos+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!pent.IsPacked)
            {
                if (input.Signature != 0x4238534C) // 'LS8B'
                    return input;
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (pent.Offset+4);
            }
            using (input)
            {
                var data = new byte[pent.UnpackedSize];
                input.Position = 0xC;
                LzssUnpack (input, data);
                return new BinMemoryStream (data);
            }
        }

        void LzssUnpack (IBinaryStream input, byte[] output)
        {
            byte[] frame = new byte[0x1000];
            int frame_pos = 0xFFF;
            const int frame_mask = 0xFFF;
            int ctl = 1;
            int dst = 0;
            while (dst < output.Length)
            {
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0x100;
                }
                if (0 == (ctl & 1))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    frame[++frame_pos & frame_mask] = (byte)b;
                    output[dst++] = (byte)b;
                }
                else
                {
                    int lo = input.ReadByte();
                    if (-1 == lo)
                        break;
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = hi << 4 | lo >> 4;
                    for (int count = 3 + (lo & 0xF); count != 0; --count)
                    {
                        byte v = frame[(offset + frame_pos++ - 0xFFF) & frame_mask];
                        frame[frame_pos & frame_mask] = v;
                        output[dst++] = v;
                    }
                }
                ctl >>= 1;
            }
        }
    }
}
