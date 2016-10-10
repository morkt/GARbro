//! \file       ArcAST.cs
//! \date       Tue Apr 21 02:24:20 2015
//! \brief      AST script engine resource archives.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AST
{
    internal class AstArchive : ArcFile
    {
        public AstArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AST"; } }
        public override string Description { get { return "AST script engine resource archive"; } }
        public override uint     Signature { get { return 0x32435241; } } // 'ARC2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "" };
            Signatures = new uint[] { 0x32435241, 0x31435241 }; // 'ARC2', 'ARC1'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (3) - 0x30;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var name_buf = new byte[32];
            var dir = new List<Entry> (count);
            long index_offset = 8;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                uint offset = next_offset;
                uint size   = file.View.ReadUInt32 (index_offset+4);
                int name_length = file.View.ReadByte (index_offset+8);
                if (name_length > name_buf.Length)
                    name_buf = new byte[name_length];
                file.View.Read (index_offset+9, name_buf, 0, (uint)name_length);
                if (i+1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset+9+name_length);
                if (0 != offset && offset != file.MaxOffset)
                {
                    if (2 == version)
                        for (int j = 0; j < name_length; ++j)
                            name_buf[j] ^= 0xff;
                    uint packed_size;
                    if (0 == next_offset)
                        packed_size = size;
                    else if (next_offset >= offset)
                        packed_size = next_offset - offset;
                    else
                        return null;
                    string name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = new PackedEntry
                    {
                        Name = name,
                        Type = FormatCatalog.Instance.GetTypeFromName (name),
                        Offset = offset,
                        Size = packed_size,
                        UnpackedSize = size,
                        IsPacked = packed_size != size,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 9 + name_length;
            }
            if (2 == version)
                return new AstArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !(arc is AstArchive))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (!pent.IsPacked)
            {
                arc.File.View.Reserve (entry.Offset, entry.Size);
                var sig = arc.File.View.ReadUInt32 (entry.Offset);
                if (0xB8B1AF76 == sig) // PNG signature ^ FF
                {
                    var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
                    for (int i = 0; i < data.Length; ++i)
                        data[i] ^= 0xff;
                    return new MemoryStream (data);
                }
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var data = UnpackLzss (input, pent.Size, pent.UnpackedSize);
                return new MemoryStream (data);
            }
        }

        byte[] UnpackLzss (Stream input, uint input_size, uint output_size)
        {
            var output = new byte[output_size];
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            const int frame_mask = 0xFFF;

            int dst = 0;
            int remaining = (int)input_size;
            int ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    if (remaining <= 0)
                        break;
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    --remaining;
                    ctl |= 0x100;
                }
                if (0 != (ctl & 1))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    --remaining;
                    frame[frame_pos++] = output[dst++] = (byte)~b;
                    frame_pos &= frame_mask;
                }
                else
                {
                    if (remaining < 2)
                        break;
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    remaining -= 2;
                    int offset = (hi & 0xf0) << 4 | lo;
                    for (int count = 3 + (hi & 0xF); count != 0; --count)
                    {
                        if (dst >= output.Length)
                            break;
                        byte v = frame[offset++];
                        offset &= frame_mask;
                        frame[frame_pos++] = v;
                        frame_pos &= frame_mask;
                        output[dst++] = v;
                    }
                }
            }
            return output;
        }
    }
}
