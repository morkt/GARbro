//! \file       ArcPACK.cs
//! \date       2018 Jan 04
//! \brief      Bonk! Game Studio resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Globalization;
using System.IO;
using System.Linq;
using GameRes.Compression;

namespace GameRes.Formats.Bonk
{
    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/BONK"; } }
        public override string Description { get { return "Bonk! Game Stduio resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var index = LookupIndex (file);
            if (null == index)
                return null;

            var last_record = index.Last();
            if (last_record.Offset + last_record.Size > file.MaxOffset)
                return null;
            var dir = index.Select (e => e.ToEntry()).ToList();
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked && (entry.Size < 16 || !arc.File.View.AsciiEqual (entry.Offset, "SLID")))
                return base.OpenEntry (arc, entry);
            if (!pent.IsPacked)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+4);
            }
            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+8);
            int frame_size = arc.File.View.ReadUInt16 (entry.Offset+0xC);
            var input = arc.File.CreateStream (entry.Offset+0x10, packed_size);
            var lzss = new LzssStream (input);
            lzss.Config.FrameSize = frame_size;
            lzss.Config.FrameInitPos = frame_size - 0x12;
            return lzss;
        }

        IEnumerable<IndexRecord> LookupIndex (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            if (!arc_name.StartsWith ("data_") || !arc_name.EndsWith (".pack"))
                return null;
            ArchiveRecord arc_record;
            if (!FileListMap.Value.TryGetValue (arc_name, out arc_record))
                return null;
            if (file.MaxOffset != arc_record.Size)
                return null;
            return arc_record.Index;
        }

        Lazy<Dictionary<string, ArchiveRecord>> FileListMap = new Lazy<Dictionary<string, ArchiveRecord>> (ReadFileList);

        static Dictionary<string, ArchiveRecord> ReadFileList ()
        {
            var file_map = new Dictionary<string, ArchiveRecord>();
            var comma = new char[] {','};
            List<IndexRecord> current_list = null;
            FormatCatalog.Instance.ReadFileList ("bonk_ntr_1.lst", line => {
                var parts = line.Split (comma);
                if (2 == parts.Length)
                {
                    current_list = new List<IndexRecord>();
                    file_map[parts[0]] = new ArchiveRecord {
                        Size = long.Parse (parts[1], NumberStyles.HexNumber),
                        Index = current_list,
                    };
                }
                else if (3 == parts.Length)
                {
                    current_list.Add (new IndexRecord {
                        Name = parts[2],
                        Offset = long.Parse (parts[0], NumberStyles.HexNumber),
                        Size   = uint.Parse (parts[1], NumberStyles.HexNumber),
                    });
                }
            });
            return file_map;
        }
    }

    /// <summary>
    /// Identifies known archive file.
    /// </summary>
    internal class ArchiveRecord
    {
        public long     Size;
        public IEnumerable<IndexRecord> Index;
    }

    internal class IndexRecord
    {
        public string   Name;
        public long     Offset;
        public uint     Size;

        public Entry ToEntry ()
        {
            var entry = FormatCatalog.Instance.Create<PackedEntry> (Name);
            entry.Offset = Offset;
            entry.Size = Size;
            return entry;
        }
    }
}
