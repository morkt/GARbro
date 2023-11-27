//! \file       ArcDAT.cs
//! \date       2018 May 04
//! \brief      Apricot resource archive.
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
using System.IO;
using System.Text;
using GameRes.Compression;

namespace GameRes.Formats.Apricot
{
    [Export(typeof(ArchiveFormat))]
    public class Mpf2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MPF2"; } }
        public override string Description { get { return "Apricot resource archive"; } }
        public override uint     Signature { get { return 0x3246504D; } } // 'MPF2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_list = new List<Entry>();
            long max_offset = file.MaxOffset;
            for (int i = 1; i < 100; ++i)
            {
                var part_name = Path.ChangeExtension (file.Name, string.Format ("a{0:D02}", i));
                if (!VFS.FileExists (part_name))
                    break;
                var part = VFS.FindFile (part_name);
                arc_list.Add (part);
                max_offset += part.Size;
            }
            uint index_length = file.View.ReadUInt32 (8);
            uint data_offset = file.View.ReadUInt32 (0x10);
            using (var zindex = file.CreateStream (0x20, index_length))
            using (var uindex = new ZLibStream (zindex, CompressionMode.Decompress))
            using (var index = new BinaryStream (uindex, file.Name))
            {
                var buffer = new byte[500];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int entry_length = index.ReadInt32();
                    if (entry_length <= 528)
                        return null;
                    bool is_deleted = index.ReadUInt32() != 0;
                    var entry = new PackedEntry();
                    entry.Offset = index.ReadInt64() + data_offset;
                    index.ReadUInt32();
                    entry.Size = index.ReadUInt32();
                    entry.UnpackedSize = index.ReadUInt32();
                    entry.IsPacked = entry.Size != entry.UnpackedSize;
                    index.Read (buffer, 0, 500);
                    int name_length = index.Read (buffer, 0, entry_length - 528);
                    if (!is_deleted && entry.CheckPlacement (max_offset))
                    {
                        entry.Name = Encoding.Unicode.GetString (buffer, 0, name_length);
                        entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                        dir.Add (entry);
                    }
                }
                var parts = new List<ArcView> (arc_list.Count);
                try
                {
                    foreach (var arc_entry in arc_list)
                    {
                        var arc_file = VFS.OpenView (arc_entry);
                        parts.Add (arc_file);
                    }
                }
                catch
                {
                    foreach (var part in parts)
                        part.Dispose();
                    throw;
                }
                return new MultiFileArchive (file, this, dir, parts);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var mpf = (MultiFileArchive)arc;
            var input = mpf.OpenStream (entry);
            var pent = entry as PackedEntry;
            if (pent != null && pent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
