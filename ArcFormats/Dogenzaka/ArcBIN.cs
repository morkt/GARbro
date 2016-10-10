//! \file       ArcBIN.cs
//! \date       Wed Jul 06 09:46:31 2016
//! \brief      Dogenzaka Lab resource archive.
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
using GameRes.Compression;

namespace GameRes.Formats.Dogenzaka
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/Dogenzaka"; } }
        public override string Description { get { return "Dogenzaka Lab audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            int index_offset = 4;
            int index_end = 4 + 8 * count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry
                {
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size   = file.View.ReadUInt32 (index_offset+4),
                };
                if (entry.Offset < index_end || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Name = string.Format ("{0}#{1:D5}", base_name, i);
                dir.Add (entry);
                index_offset += 8;
            }
            foreach (PackedEntry entry in dir)
            {
                var n = file.View.ReadInt32 (entry.Offset);
                if (n <= 0)
                    return null;
                var offset = file.View.ReadUInt32 (entry.Offset+4);
                var size   = file.View.ReadUInt32 (entry.Offset+8);
                entry.Offset += offset;
                entry.Size = size & 0x3FFFFFFF;
                entry.IsPacked = 2 != (size >> 30);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                var res = AutoEntry.DetectFileType (file.View.ReadUInt32 (entry.Offset));
                if (res != null)
                {
                    entry.Type = res.Type;
                    var ext = res.Extensions.FirstOrDefault();
                    if (!string.IsNullOrEmpty (ext))
                        entry.Name = Path.ChangeExtension (entry.Name, ext);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class GamedatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/Dogenzaka/2"; } }
        public override string Description { get { return "Dogenzaka Lab archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GamedatOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count-1))
                return null;
            uint base_offset = (uint)(4 + 4 * count);
            if (base_offset >= file.MaxOffset)
                return null;

            uint index_offset = 4;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            if (next_offset != 0)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            next_offset = base_offset;
            --count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = AutoEntry.Create (file, next_offset, name);
                next_offset = base_offset + file.View.ReadUInt32 (index_offset);
                if (next_offset > file.MaxOffset)
                    return null;
                entry.Size = next_offset - (uint)entry.Offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
