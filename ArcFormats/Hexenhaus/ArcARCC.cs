//! \file       ArcARCC.cs
//! \date       Sat Mar 05 10:24:00 2016
//! \brief      Hexenhaus resource archive.
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
using System.Linq;

namespace GameRes.Formats.Hexenhaus
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARCC"; } }
        public override string Description { get { return "Hexenhaus resource archive"; } }
        public override uint     Signature { get { return 0x43435241; } } // 'ARCC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x14);
            if (!IsSaneCount (count))
                return null;
            long index_offset = 0x2A;
            if (!file.View.AsciiEqual (index_offset, "NAME"))
                return null;
            var addr_offset = file.View.ReadInt64 (index_offset+4);
            index_offset += 0xE;
            if (!file.View.AsciiEqual (index_offset, "NIDX"))
                return null;
            index_offset += 4;
            var nidx_offsets = new uint[count]; // name offsets
            for (int i = 0; i < count; ++i)
            {
                nidx_offsets[i] = file.View.ReadUInt32 (index_offset+2);
                index_offset += 8;
            }
            if (!file.View.AsciiEqual (index_offset, "EIDX"))
                return null;
            index_offset += 4 + 8 * count;
            if (!file.View.AsciiEqual (index_offset, "CINF"))
                return null;
            index_offset += 4;
            var name_buffer = new byte[0x40];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 6;
                ushort name_length = file.View.ReadUInt16 (index_offset);
                if (name_length > name_buffer.Length)
                    name_buffer = new byte[name_length];
                file.View.Read (index_offset+4, name_buffer, 0, name_length);
                index_offset += 6 + name_length;
                var name = DecryptName (name_buffer, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                dir.Add (entry);
            }
            index_offset = addr_offset;
            if (!file.View.AsciiEqual (index_offset, "ADDR"))
                return null;
            index_offset += 4;
            for (int i = 0; i < count; ++i)
            {
                dir[i].Offset = file.View.ReadInt64 (index_offset+2);
                index_offset += 12;
            }
            foreach (var entry in dir)
            {
                if (!file.View.AsciiEqual (entry.Offset, "FILE"))
                    continue;
                entry.Size = file.View.ReadUInt32 (entry.Offset+0x18);
                entry.Offset += 0x22;
            }
            dir = dir.Where (entry => entry.Size != 0).ToList();
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        static string DecryptName (byte[] name, int length)
        {
            for (int i = 0; i < length; ++i)
                name[i] ^= 0x69;
            return Encodings.cp932.GetString (name, 0, length);
        }
    }
}
