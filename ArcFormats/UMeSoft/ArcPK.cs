//! \file       ArcPK.cs
//! \date       Wed Jul 15 00:19:23 2015
//! \brief      U-Me soft resource archives.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.UMeSoft
{
    [Export(typeof(ArchiveFormat))]
    public class PkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PK"; } }
        public override string Description { get { return "U-Me Soft resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PkOpener ()
        {
            Extensions = new string[] { "pk", "gpk", "tpk", "wpk", "mpk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_end = file.MaxOffset - 4;
            uint index_size = file.View.ReadUInt32 (index_end);
            if (0 == index_size || index_size >= index_end)
                return null;

            long index_offset = index_end - index_size;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;

            var dir = new List<Entry>();
            while (index_offset < index_end)
            {
                uint name_len = file.View.ReadByte (index_offset++);
                if (0 == name_len)
                    break;
                if (name_len+14 > index_end-index_offset)
                    return null;
                string name = file.View.ReadString (index_offset, name_len);
                index_offset += name_len+6;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (index_offset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith (".scr", StringComparison.InvariantCultureIgnoreCase) &&
                !entry.Name.EndsWith (".tbl", StringComparison.InvariantCultureIgnoreCase))
                return base.OpenEntry (arc, entry);
            int output_size = arc.File.View.ReadInt32 (entry.Offset);
            if (output_size <= 0)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset+4, entry.Size-4))
            {
                var data = LzUnpack (input, output_size);
                for (int i = 0; i < data.Length; ++i)
                    data[i] ^= 0x42;
                return new MemoryStream (data);
            }
        }

        byte[] LzUnpack (Stream input, int output_size)
        {
            var output = new byte[output_size];
            int ctl = 0;
            int mask = 0;
            int dst = 0;
            while (dst < output_size)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    mask = 0x80;
                }
                if (0 == (ctl & mask))
                {
                    output[dst++] = (byte)input.ReadByte();
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == lo || -1 == hi)
                        break;
                    int offset = hi << 4 | lo >> 4;
                    if (0 == offset)
                        break;
                    int count  = (lo & 0xF) + 3;
                    Binary.CopyOverlapped (output, dst - offset, dst, count);
                    dst += count;
                }
            }
            return output;
        }
    }
}
