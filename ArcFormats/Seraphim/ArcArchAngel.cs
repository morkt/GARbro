//! \file       ArcArchAngel.cs
//! \date       2022 May 01
//! \brief      ArchAngel engine resource archive.
//
// Copyright (C) 2022 by morkt
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

// [960920][Petit] Trouble Outsiders
// [980220][Euphony Production] Happening Journey

namespace GameRes.Formats.ArchAngel
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/ARCH"; } }
        public override string Description { get { return "ArchAngel engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            ContainedFormats = new[] { "CB" };
        }

        static readonly string[] DefaultSections = { "image", "script", "" };

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset > uint.MaxValue
                || !VFS.IsPathEqualsToFileName (file.Name, "ARCHPAC.DAT"))
                return null;
            int file_count = file.View.ReadInt16 (0);
            if (!IsSaneCount (file_count))
                return null;
            long index_pos = 2 + 4 * file_count;
            var section_table = new SortedDictionary<int, uint>();
            uint min_offset = (uint)file.MaxOffset;
            while (index_pos + 6 <= min_offset)
            {
                uint offset = file.View.ReadUInt32 (index_pos);
                int index = file.View.ReadInt16 (index_pos+4);
                if (index < 0 || index > file_count || offset > file.MaxOffset)
                    return null;
                if (offset < min_offset)
                    min_offset = offset;
                section_table[index] = offset;
                index_pos += 6;
            }
            var dir = new List<Entry> (file_count);
            int section_num = 0;
            Func<string> get_type;
            if (section_table.Count == DefaultSections.Length)
                get_type = () => DefaultSections[section_num];
            else
                get_type = () => section_num > 0 ? "image" : "";
            foreach (var section in section_table)
            {
                int i = section.Key;
                uint base_offset = section.Value;
                do
                {
                    uint size = file.View.ReadUInt32 (2 + i * 4);
                    if (size > 0)
                    {
                        var entry = new PackedEntry
                        {
                            Name = string.Format("{0}-{1:D6}", section_num, i),
                            Type = get_type(),
                            Offset = base_offset,
                            Size = size,
                        };
                        if (!entry.CheckPlacement(file.MaxOffset))
                            return null;
                        if ("script" == entry.Type)
                            entry.IsPacked = true;
                        dir.Add(entry);
                        base_offset += size;
                    }
                    ++i;
                }
                while (i < file_count && !section_table.ContainsKey (i));
                ++section_num;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || pent.Size <= 4)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (0 == pent.UnpackedSize)
                pent.UnpackedSize = input.Signature;
            try
            {
                var data = Seraphim.ScnOpener.LzDecompress (input);
                return new BinMemoryStream (data, entry.Name);
            }
            catch
            {
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            finally
            {
                input.Dispose();
            }
        }
    }
}
