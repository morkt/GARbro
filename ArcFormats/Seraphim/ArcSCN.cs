//! \file       ArcSCN.cs
//! \date       2017 Nov 25
//! \brief      Seraphim engine scripts archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class ScnOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SERAPH/SCN"; } }
        public override string Description { get { return "Seraphim engine scripts archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ScnOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "SCNPAC.DAT"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 4 * (uint)count;
            if (index_size > file.View.Reserve (4, index_size))
                return null;

            int index_offset = 4;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            if (next_offset < index_offset + index_size)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry { Name = i.ToString ("D5"), Type = "script" };
                entry.Offset = next_offset;
                next_offset = file.View.ReadUInt32 (index_offset);
                if (next_offset < entry.Offset || next_offset > file.MaxOffset)
                    return null;
                entry.Size = next_offset - (uint)entry.Offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == input.Signature || 0 != (input.Signature & 0xFF000000))
                return input;
            try
            {
                var data = LzDecompress (input);
                input.Dispose();
                return new BinMemoryStream (data, entry.Name);
            }
            catch
            {
                input.Position = 0;
                return input;
            }
        }

        internal byte[] LzDecompress (IBinaryStream input)
        {
            int unpacked_size = input.ReadInt32();
            var data = new byte[unpacked_size];
            int dst = 0;
            while (dst < unpacked_size)
            {
                int ctl = input.ReadByte();
                if (-1 == ctl)
                    throw new EndOfStreamException();
                if (0 != (ctl & 0x80))
                {
                    byte lo = input.ReadUInt8();
                    int offset = ((ctl << 3 | lo >> 5) & 0x3FF) + 1;
                    int count = (lo & 0x1F) + 1;
                    Binary.CopyOverlapped (data, dst-offset, dst, count);
                    dst += count;
                }
                else
                {
                    int count = ctl + 1;
                    if (input.Read (data, dst, count) != count)
                        throw new EndOfStreamException();
                    dst += count;
                }
            }
            return data;
        }
    }
}
