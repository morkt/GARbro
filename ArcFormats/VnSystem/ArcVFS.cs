//! \file       ArcVFS.cs
//! \date       Wed Jan 18 21:30:30 2017
//! \brief      VNSystem engine resource archive.
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

namespace GameRes.Formats.VnSystem
{
    [Export(typeof(ArchiveFormat))]
    public class VfsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VFS/VNSYSTEM"; } }
        public override string Description { get { return "VNSystem engine resource archive"; } }
        public override uint     Signature { get { return 0x20534656; } } // 'VFS File'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "File"))
                return null;
            bool is_compressed = file.View.ReadInt32 (8) != 0;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x10;
            long data_offset = index_offset + count * 0x1C;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x14);
                index_offset += 0x14;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = data_offset + file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = is_compressed;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
            {
                var data = UnpackEntry (input, pent.UnpackedSize);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        byte[] UnpackEntry (Stream input, uint unpacked_size)
        {
            const int dict_size = 0x10;
            var output = new byte[unpacked_size];
            using (var bits = new MsbBitStream (input, true))
            {
                var dict = new byte[dict_size];
                int dict_pos = 0;
                for (int dst = 0; dst < output.Length; ++dst)
                {
                    byte cur_byte;
                    if (bits.GetNextBit() != 0)
                    {
                        int offset = GetBitLength (bits);
                        int pos = dict_pos - offset;
                        if (pos < 0)
                            pos += dict_size;
                        if (pos < 0 || pos >= dict_size)
                            throw new InvalidDataException ("Invalid compressed data.");
                        cur_byte = dict[pos];
                    }
                    else
                    {
                        cur_byte = (byte)bits.GetBits (8);
                    }
                    output[dst] = cur_byte;
                    dict[dict_pos++] = cur_byte;
                    dict_pos &= 0xF;
                }
            }
            return output;
        }

        static int GetBitLength (IBitStream input)
        {
            int count = 0;
            while (0 == input.GetNextBit())
                ++count;
            int n = 1 << count;
            if (count > 0)
                n |= input.GetBits (count);
            return n;
        }
    }
}
