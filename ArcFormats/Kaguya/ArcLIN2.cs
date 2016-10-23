//! \file       ArcLIN2.cs
//! \date       Sun Oct 23 09:57:27 2016
//! \brief      KaGuYa archive format.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ArchiveFormat))]
    public class Lin2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/LIN2"; } }
        public override string Description { get { return "KaGuYa script engine resource archive"; } }
        public override uint     Signature { get { return 0x324E494C; } } // 'LIN2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var name_buffer = new byte[0x100];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                ushort name_length = file.View.ReadUInt16 (index_offset);
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                file.View.Read (index_offset+2, name_buffer, 0, name_length);
                for (int j = 0; j < name_length; ++j)
                    name_buffer[j] ^= 0xFF;
                var name = Binary.GetCString (name_buffer, 0, name_length);
                if (string.IsNullOrEmpty (name))
                    return null;
                index_offset += 2u + name_length;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                int type = file.View.ReadInt16 (index_offset+8);
                if (1 == type)
                    entry.IsPacked = true;
                else if (2 == type)
                    entry.Type = "audio";
                dir.Add (entry);
                index_offset += 10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
            {
                var data = UnpackLzss (input, pent.UnpackedSize);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        byte[] UnpackLzss (IBinaryStream input, uint unpacked_size)
        {
            var output = new byte[unpacked_size];
            var frame = new byte[0x100];
            int frame_pos = 0xEF;
            int dst = 0;
            int ctl = 0;
            int bit = 0;
            int prev_count = -1;
            while (dst < output.Length)
            {
                bit >>= 1;
                if (0 == bit)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    bit = 0x80;
                }
                if (0 != (ctl & bit))
                {
                    byte v = input.ReadUInt8();
                    frame[frame_pos++ & 0xFF] = v;
                    output[dst++] = v;
                }
                else
                {
                    int offset = input.ReadUInt8();
                    int count;
                    if (-1 == prev_count)
                    {
                        prev_count = input.ReadUInt8();
                        count = prev_count & 0xF;
                    }
                    else
                    {
                        count = prev_count >> 4;
                        prev_count = -1;
                    }
                    count += 2;
                    while (count --> 0 && dst < output.Length)
                    {
                        byte v = frame[offset++ & 0xFF];
                        frame[frame_pos++ & 0xFF] = v;
                        output[dst++] = v;
                    }
                }
            }
            return output;
        }
    }
}
