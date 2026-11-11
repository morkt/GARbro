//! \file       ArcDAT.cs
//! \date       2026-09-30
//! \brief      Artdink (PlayStation 2) PIDX resource archive.
//
// Copyright (C) 2026 by morkt
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
// Galaxy Angel (PS2) resource archives: entries are addressed by a directory table
// and by FSTS subtables; payloads may be ARZ-compressed.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Artdink
{
    internal class DatEntry : Entry
    {
        public uint PackedSize;
        public uint UnpackedSize;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/ARTDINK"; } }
        public override string Description { get { return "Artdink PS2 resource archive"; } }
        public override uint     Signature { get { return 0x58444950; } } // 'PIDX'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
            ContainedFormats = new[] { "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x28 || !file.View.AsciiEqual (0, "PIDX"))
                return null;
            uint table2_count = file.View.ReadUInt32 (0x10);
            uint root_count   = file.View.ReadUInt32 (0x14);
            uint table3_size  = file.View.ReadUInt32 (0x1C);
            if (0 == table2_count && 0 == table3_size)
                return null;
            if (table2_count >= 0x40000)
                return null;
            var dir = new List<Entry> ();
            var names = new HashSet<string> ();
            if (table2_count > 0 && !ReadTable2 (file, dir, names, table2_count, root_count))
                return null;
            if (table3_size > 0)
                ReadTable3 (file, dir, names);
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var dent = (DatEntry)entry;
            var file = arc.File;
            var data = file.View.ReadBytes (entry.Offset, dent.PackedSize);
            if (TryDecompress (data, out byte[] output))
                return new BinMemoryStream (output, entry.Name);
            long raw_size = dent.UnpackedSize > 0 ? dent.UnpackedSize : dent.PackedSize;
            raw_size = Math.Min (raw_size, file.MaxOffset - entry.Offset);
            if (raw_size > int.MaxValue)
                raw_size = int.MaxValue;
            if (raw_size <= 0)
                return Stream.Null;
            var raw = file.View.ReadBytes (entry.Offset, (uint)raw_size);
            return new BinMemoryStream (raw, entry.Name);
        }

        // Directory table: 24-byte records holding the name offset (into the string
        // pool) and either child range (directories) or payload address and sizes.
        bool ReadTable2 (ArcView file, List<Entry> dir, HashSet<string> names, uint count, uint root_count)
        {
            long table_offset = file.View.ReadUInt32 (0x0C);
            long string_pool  = file.View.ReadUInt32 (0x20);
            long table_end = table_offset + (long)count * 24;
            if (table_offset < 0 || table_end > file.MaxOffset)
                return false;
            var records = new Table2Record[(int)count];
            for (int i = 0; i < (int)count; ++i)
            {
                long offset = table_offset + i * 24;
                var record = new Table2Record ();
                record.Name = ReadString (file, string_pool + file.View.ReadUInt32 (offset + 4));
                record.IsDirectory = 1 == file.View.ReadUInt32 (offset);
                if (record.IsDirectory)
                {
                    record.ChildCount = file.View.ReadUInt32 (offset + 8);
                    record.ChildStart = file.View.ReadUInt32 (offset + 0xC);
                }
                else
                {
                    record.DataOffset = file.View.ReadUInt32 (offset + 0xC);
                    record.UnpackedSize = file.View.ReadUInt32 (offset + 0x10);
                    record.PackedSize = file.View.ReadUInt32 (offset + 0x14);
                }
                records[i] = record;
            }
            var paths = new Dictionary<int, string> ();
            var visited = new HashSet<int> ();
            int root = (int)Math.Min (root_count, count);
            if (root > 0)
            {
                for (int i = 0; i < root; ++i)
                    BuildPaths (records, i, string.Empty, paths, visited);
            }
            else
            {
                BuildPaths (records, 0, string.Empty, paths, visited);
            }
            foreach (var path in paths)
            {
                var record = records[path.Key];
                if (!record.IsDirectory)
                    AddFile (file, dir, names, path.Value, record.DataOffset, record.PackedSize, record.UnpackedSize);
            }
            for (int i = 0; i < (int)count; ++i)
            {
                if (visited.Contains (i) || records[i].IsDirectory)
                    continue;
                AddFile (file, dir, names, records[i].Name, records[i].DataOffset, records[i].PackedSize, records[i].UnpackedSize);
            }
            return true;
        }

        static void BuildPaths (Table2Record[] records, int index, string parent, Dictionary<int, string> paths, HashSet<int> visited)
        {
            if (index < 0 || index >= records.Length || !visited.Add (index))
                return;
            var record = records[index];
            string path = parent.Length > 0 ? parent + "/" + record.Name : record.Name;
            paths[index] = path;
            if (!record.IsDirectory)
                return;
            for (uint i = 0; i < record.ChildCount; ++i)
                BuildPaths (records, (int)(record.ChildStart + i), path, paths, visited);
        }

        // FSTS subtables: a pointer list, each pointing to a header with an entry
        // array and a string pool, all offsets relative to the subtable start.
        void ReadTable3 (ArcView file, List<Entry> dir, HashSet<string> names)
        {
            long table_offset = file.View.ReadUInt32 (0x18);
            uint table_size  = file.View.ReadUInt32 (0x1C);
            if (table_offset + 4 > file.MaxOffset || table_offset + table_size > file.MaxOffset)
                return;
            uint count = file.View.ReadUInt32 (table_offset);
            if (count >= 0x40000 || table_offset + 4 + (long)count * 4 > file.MaxOffset)
                return;
            for (uint i = 0; i < count; ++i)
            {
                long pointer = file.View.ReadUInt32 (table_offset + 4 + i * 4) + table_offset;
                if (pointer + 12 > file.MaxOffset)
                    continue;
                long fsts_offset = file.View.ReadUInt32 (pointer + 8);
                if (fsts_offset + 16 > file.MaxOffset || !file.View.AsciiEqual (fsts_offset, "FSTS"))
                    continue;
                uint entries_count = file.View.ReadUInt32 (fsts_offset + 4);
                uint entries_offset = file.View.ReadUInt32 (fsts_offset + 8);
                uint string_pool_offset = file.View.ReadUInt32 (fsts_offset + 12);
                long entries_start = fsts_offset + entries_offset;
                if (entries_count >= 0x40000 || entries_start + (long)entries_count * 16 > file.MaxOffset)
                    continue;
                for (uint j = 0; j < entries_count; ++j)
                {
                    long entry_offset = entries_start + j * 16;
                    string name = ReadString (file, fsts_offset + string_pool_offset + file.View.ReadUInt32 (entry_offset));
                    name = name.Replace ('\\', '/').TrimStart ('/');
                    uint unpacked = file.View.ReadUInt32 (entry_offset + 8);
                    uint packed = file.View.ReadUInt32 (entry_offset + 12);
                    long data_offset = fsts_offset + file.View.ReadUInt32 (entry_offset + 4);
                    AddFile (file, dir, names, name, data_offset, packed, unpacked);
                }
            }
        }

        void AddFile (ArcView file, List<Entry> dir, HashSet<string> names, string path, long offset, uint packed, uint unpacked)
        {
            if (string.IsNullOrWhiteSpace (path) || !names.Add (path))
                return;
            var entry = Create<DatEntry> (path);
            entry.Offset = offset;
            // uncompressed entries store the payload size in the unpacked field only
            entry.Size = packed > 0 ? packed : unpacked;
            entry.PackedSize = packed;
            entry.UnpackedSize = unpacked;
            if (entry.Name.HasAnyOfExtensions ("scn", "isb", "asb"))
                entry.Type = "script";
            if (!entry.CheckPlacement (file.MaxOffset))
            {
                names.Remove (path);
                return;
            }
            dir.Add (entry);
        }

        static string ReadString (ArcView file, long offset)
        {
            if (offset < 0 || offset >= file.MaxOffset)
                return string.Empty;
            uint size = (uint)Math.Min (0x1000, file.MaxOffset - offset);
            var bytes = file.View.ReadBytes (offset, size);
            int length = Array.IndexOf (bytes, (byte)0);
            if (length < 0)
                length = bytes.Length;
            return length > 0 ? Encodings.cp932.GetString (bytes, 0, length) : string.Empty;
        }

        // Payloads are optionally ARZ-compressed: "ARZ" or " 3;" prefix, a hex mode
        // character, the unpacked size, then the payload XORed with 0x72; mode 1 is
        // LZSS, mode 0 is a plain copy.
        static bool TryDecompress (byte[] data, out byte[] output)
        {
            output = null;
            if (data.Length < 8)
                return false;
            int mode = ParseHex (data[3]);
            if (mode < 0 || mode > 1)
                return false;
            bool has_prefix = ('A' == data[0] && 'R' == data[1] && 'Z' == data[2])
                           || (' ' == data[0] && '3' == data[1] && ';' == data[2]);
            if (!has_prefix)
                return false;
            uint unpacked_size = LittleEndian.ToUInt32 (data, 4);
            if (0 == unpacked_size || unpacked_size > int.MaxValue)
                return false;
            int payload_size = data.Length - 8;
            var payload = new byte[payload_size];
            for (int i = 0; i < payload_size; ++i)
                payload[i] = (byte)(data[i+8] ^ 0x72);
            if (0 == mode)
            {
                int size = Math.Min (payload_size, (int)unpacked_size);
                output = new byte[size];
                Buffer.BlockCopy (payload, 0, output, 0, size);
                return true;
            }
            using (var mem = new MemoryStream (payload, false))
            using (var lzss = new LzssReader (mem, payload_size, (int)unpacked_size))
            {
                lzss.Unpack ();
                output = lzss.Data;
            }
            return true;
        }

        static int ParseHex (byte value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            return -1;
        }

        class Table2Record
        {
            public string Name;
            public bool IsDirectory;
            public uint ChildCount;
            public uint ChildStart;
            public uint DataOffset;
            public uint UnpackedSize;
            public uint PackedSize;
        }
    }
}