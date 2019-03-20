//! \file       ArcDAT.cs
//! \date       2019 Mar 18
//! \brief      Jam Creation resource archive.
//
// Copyright (C) 2019 by morkt
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

// [060127][ainos] Pachi Pachi Circuit

namespace GameRes.Formats.JamCreation
{
    internal class AinosEntry : PackedEntry
    {
        public bool IsEncrypted;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/JAM"; } }
        public override string Description { get { return "Jam Creation resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat") || VFS.IsPathEqualsToFileName (file.Name, "00000000.dat"))
                return null;
            var index_name = VFS.ChangeFileName (file.Name, "00000000.dat");
            if (!VFS.FileExists (index_name))
                return null;
            var arc_name = Path.GetFileName (file.Name);
            using (var index = VFS.OpenView (index_name))
            {
                var dir = ReadIndex (index, arc_name, file.MaxOffset);
                if (null == dir)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as AinosEntry;
            if (null == pent)
                return input;
            if (pent.IsPacked)
                return new ZLibStream (input, CompressionMode.Decompress);
            if (!pent.IsEncrypted)
                return input;
            using (input)
            {
                var data = input.ReadBytes ((int)entry.Size);
                Decrypt (data, data.Length);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        List<Entry> ReadIndex (ArcView file, string arc_name, long arc_length)
        {
            if (file.View.ReadUInt32 (0x14) != 1)
                return null;
            uint table_pos = 0x18;
            var indexDir = new List<Entry> (5);
            for (int i = 0; i < 5; ++i)
            {
                var entry = new Entry {
                    Offset = file.View.ReadUInt32 (table_pos),
                    Size   = file.View.ReadUInt32 (table_pos+4),
                };
                if (entry.Size < 4 || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                indexDir.Add (entry);
                table_pos += 8;
            }
            int count = file.View.ReadInt32 (indexDir[0].Offset);
            if (!IsSaneCount (count) || 24 * count > indexDir[0].Size - 4)
                return null;
            var index = file.View.ReadBytes (indexDir[0].Offset+4, indexDir[0].Size-4);
            Decrypt (index, 24 * count);

            int name_count = file.View.ReadInt32 (indexDir[1].Offset);
            if (4 * name_count > indexDir[1].Size - 4)
                return null;
            var names_index = file.View.ReadBytes (indexDir[1].Offset+4, indexDir[1].Size-4);
            Decrypt (names_index, 4 * name_count);

            var names = file.View.ReadBytes (indexDir[2].Offset, indexDir[2].Size);
            Decrypt (names, (int)indexDir[2].Size);

            int arc_count = file.View.ReadInt32 (indexDir[3].Offset);
            if (4 * arc_count > indexDir[3].Size - 4)
                return null;
            var arc_names_index = file.View.ReadBytes (indexDir[3].Offset+4, indexDir[3].Size-4);
            Decrypt (arc_names_index, 4 * arc_count);

            var arc_names_data = file.View.ReadBytes (indexDir[4].Offset, indexDir[4].Size);
            Decrypt (arc_names_data, (int)indexDir[4].Size);
            var arc_names = new Dictionary<string, int> (arc_count);
            for (int i = 0; i < arc_count; ++i)
            {
                int pos = arc_names_index.ToInt32 (i * 4);
                var name = Binary.GetCString (arc_names_data, pos);
                arc_names[name] = i;
            }
            if (!arc_names.ContainsKey (arc_name))
                return null;

            int arc_id = arc_names[arc_name];
            var dir = new List<Entry>();
            string subdir_name = "";
            int index_pos = 0;
            for (int i = 0; i < count; ++i)
            {
                uint flags = index.ToUInt32 (index_pos+0x10);
                if (flags == 0x80000000) // directory
                {
                    int name_pos = names_index.ToInt32 (i * 4);
                    subdir_name = Binary.GetCString (names, name_pos);
                    int subdir_count = index.ToInt32 (index_pos+0x14);
                }
                else
                {
                    int id = index.ToInt32 (index_pos);
                    if (id == arc_id)
                    {
                        int name_pos = names_index.ToInt32 (i * 4);
                        var name = Path.Combine (subdir_name, Binary.GetCString (names, name_pos));
                        var entry = Create<AinosEntry> (name);
                        entry.Offset = index.ToUInt32 (index_pos+4);
                        entry.Size = index.ToUInt32 (index_pos+8);
                        entry.UnpackedSize = index.ToUInt32 (index_pos+12);
                        entry.IsPacked = (flags & 0x100) != 0;
                        entry.IsEncrypted = (flags & 0x200) != 0;
                        if (entry.CheckPlacement (arc_length))
                            dir.Add (entry);
                    }
                }
                index_pos += 24;
            }
            if (0 == dir.Count)
                return null;
            return dir;
        }

        void Decrypt (byte[] data, int length)
        {
            byte prev = data[0];
            for (int i = 1; i < length; ++i)
            {
                data[i] -= (byte)(i + prev);
                prev = data[i];
            }
        }
    }
}
