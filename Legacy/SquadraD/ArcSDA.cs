//! \file       ArcSDA.cs
//! \date       2023 Sep 26
//! \brief      Squadra D resource archive format.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.SquadraD
{
    [Export(typeof(ArchiveFormat))]
    public class SdaOpener : ArchiveFormat
    {
        public override string         Tag => "SDA/SD";
        public override string Description => "Squadra D resource archive";
        public override uint     Signature => 0x4153; // 'SA'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public SdaOpener ()
        {
            Signatures = new[] { 0x4153u, 0xCC004153u, 0u };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SA\0"))
                return null;
            int data_offset = file.View.ReadInt32 (4);
            if (data_offset <= 8 || data_offset >= file.MaxOffset)
                return null;
            int count = (data_offset - 8) / 0x14;
            if (!IsSaneCount (count))
                return null;
            string arc_name = Path.GetFileNameWithoutExtension (file.Name);
            bool is_cg = arc_name == "g";
            uint index = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index, 0x10).Trim();;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index+0xC) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index+0x10);
                if (is_cg)
                    entry.Type = "image";
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index += 0x14;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var data = LzssDecompress (input);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        internal static byte[] LzssDecompress (IBinaryStream input)
        {
            int unpacked_size = input.ReadInt32();
            var output = new byte[unpacked_size];
            using (var bits = new LsbBitStream (input.AsStream, true))
            {
                var frame = new byte[0x1000];
                int frame_pos = 0xFC0;
                int dst = 0;
                while (dst < unpacked_size)
                {
                    if (bits.GetNextBit() == 0)
                    {
                        byte b = (byte)bits.GetBits (8);
                        output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                    }
                    else
                    {
                        int count_len = 4;
                        if (bits.GetNextBit() != 0)
                            count_len = 6;
                        int offset = bits.GetBits (12);
                        int count = bits.GetBits (count_len);
                        count = Math.Min (count + 3, unpacked_size - dst);
                        while (count --> 0)
                        {
                            byte b = frame[offset++ & 0xFFF];
                            output[dst++] = frame[frame_pos++ & 0xFFF] = b;
                        }
                    }
                }
                return output;
            }
        }
    }
}
