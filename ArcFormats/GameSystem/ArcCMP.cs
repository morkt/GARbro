//! \file       ArcCMP.cs
//! \date       Sun Jan 15 15:44:39 2017
//! \brief      'Game System' engine resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ArchiveFormat))]
    public class CmpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CMP"; } }
        public override string Description { get { return "'GameSystem' engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 8 || !file.View.AsciiEqual (file.MaxOffset-8, "PACK"))
                return null;
            uint index_offset = file.View.ReadUInt32 (file.MaxOffset-4);
            if (index_offset >= file.MaxOffset)
                return null;
            int index_size = file.View.ReadInt32 (index_offset);
            var index = new byte[index_size];
            using (var input = file.CreateStream (index_offset+4))
                LzUnpack (input, index);
            var dir = new List<Entry>();
            int index_pos = 0;
            uint offset = LittleEndian.ToUInt32 (index, index_pos);
            while (index_pos < index.Length)
            {
                index_pos += 4;
                int name_length = index[index_pos];
                if (0 == name_length)
                    break;
                bool is_packed = index[index_pos+1] != 0;
                index_pos += 6;
                name_length *= 2;
                var name = Encoding.Unicode.GetString (index, index_pos, name_length);
                index_pos += name_length;
                uint next_offset = LittleEndian.ToUInt32 (index, index_pos);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = offset;
                entry.Size = next_offset - offset;
                entry.IsPacked = is_packed;
                if (!entry.CheckPlacement (index_offset))
                    return null;
                dir.Add (entry);
                offset = next_offset;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        void LzUnpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                byte ctl = input.ReadUInt8();
                if (0 != (ctl & 0x80))
                {
                    int num = input.ReadUInt8() + (ctl << 8);
                    int offset = num & 0x7FF;
                    int count = Math.Min (((num >> 10) & 0x1E) + 2, output.Length - dst);
                    Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                    dst += count;
                }
                else
                {
                    int count = Math.Min (ctl + 1, output.Length - dst);
                    input.Read (output, dst, count);
                    dst += count;
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
            var data = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
                LzUnpack (input, data);
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
