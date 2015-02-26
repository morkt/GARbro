//! \file       ArcDRS.cs
//! \date       Thu Aug 21 06:11:09 2014
//! \brief      Digital Romance System archive implementation.
//
// Copyright (C) 2014 by morkt
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

using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Formats.Strings;

namespace GameRes.Formats.DRS
{
    [Export(typeof(ArchiveFormat))]
    public class DrsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DRS"; } }
        public override string Description { get { return "Digital Romance System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DrsOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue)
                return null;
            int dir_size = file.View.ReadUInt16 (0);
            if (dir_size < 0x20 || 0 != (dir_size & 0xf) || dir_size + 2 >= file.MaxOffset)
                return null;
            byte first = file.View.ReadByte (2);
            if (0 == first)
                return null;
            file.View.Reserve (0, (uint)dir_size + 2);
            int dir_offset = 2;

            uint next_offset = file.View.ReadUInt32 (dir_offset+12);
            if (next_offset > file.MaxOffset || next_offset < dir_size+2)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            int count = dir_size / 0x10 - 1;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                uint offset = next_offset;
                dir_offset += 0x10;
                next_offset = file.View.ReadUInt32 (dir_offset+12);
                if (next_offset > file.MaxOffset || next_offset < offset)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = offset;
                entry.Size = next_offset - offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class MpxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IKURA/GDL"; } }
        public override string Description { get { return "IKURA GDL resource archive"; } }
        public override uint     Signature { get { return 0x4d324d53; } } // 'SM2M'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public MpxOpener ()
        {
            Extensions = Enumerable.Empty<string>(); // DRS archives have no extensions
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PX10") || file.MaxOffset > uint.MaxValue)
                return null;
            int count = file.View.ReadInt32 (8);
            if (count <= 0 || count > 0xfffff)
                return null;
            uint index_size = file.View.ReadUInt32 (12);
            if (index_size > file.MaxOffset)
                return null;
            var encoding = Encodings.cp932.WithFatalFallback();
            byte[] name_raw = new byte[12];

            long dir_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (dir_offset, name_raw, 0, 12);
                int name_length = name_raw.Length;
                while (name_length > 0 && 0 == name_raw[name_length-1])
                    --name_length;
                if (0 == name_length)
                    return null;
                string name = encoding.GetString (name_raw, 0, name_length).ToLowerInvariant();
                var entry = FormatCatalog.Instance.CreateEntry (name);
                entry.Offset = file.View.ReadUInt32 (dir_offset+12);
                entry.Size   = file.View.ReadUInt32 (dir_offset+16);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                dir_offset += 0x14;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
