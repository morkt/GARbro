//! \file       ArcTCD3.cs
//! \date       Thu Oct 08 13:14:57 2015
//! \brief      TopCat data archives (TCD)
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
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.TopCat
{
    internal class TcdSection
    {
        public uint DataSize;
        public uint IndexOffset;
        public int  DirCount;
        public int  DirNameLength;
        public int  FileCount;
        public int  FileNameLength;
    }

    internal struct TcdDirEntry
    {
        public int  FileCount;
        public int  NamesOffset;
        public int  FirstIndex;
    }

    internal class TcdEntry : AutoEntry
    {
        public int Index;

        public TcdEntry (int index, string name, ArcView file, long offset)
            : base (name, () => DetectFileType (file, offset))
        {
            Index = index;
            Offset = offset;
        }

        private static IResource DetectFileType (ArcView file, long offset)
        {
            uint signature = file.View.ReadUInt32 (offset);
            return FormatCatalog.Instance.LookupSignature (signature).FirstOrDefault();
        }
    }

    internal class TcdArchive : ArcFile
    {
        public int? Key;

        public TcdArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Serializable]
    public class TcdScheme : ResourceScheme
    {
        public Dictionary<string, int> KnownKeys;
    }

    [Export(typeof(ArchiveFormat))]
    public class TcdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TCD3"; } }
        public override string Description { get { return "TopCat data archive"; } }
        public override uint     Signature { get { return 0x33444354; } } // 'TCD3'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public TcdOpener ()
        {
            Extensions = new string[] { "tcd" };
        }

        public static Dictionary<string, int> KnownKeys = new Dictionary<string, int>();

        public override ResourceScheme Scheme
        {
            get { return new TcdScheme { KnownKeys = KnownKeys }; }
            set { KnownKeys = ((TcdScheme)value).KnownKeys; }
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint current_offset = 8;
            var sections = new List<TcdSection> (5);
            for (int i = 0; i < 5; ++i, current_offset += 0x20)
            {
                uint index_offset = file.View.ReadUInt32 (current_offset+4);
                if (0 == index_offset)
                    continue;
                var section = new TcdSection
                {
                    IndexOffset     = index_offset,
                    DataSize        = file.View.ReadUInt32 (current_offset),
                    DirCount        = file.View.ReadInt32 (current_offset+8),
                    DirNameLength   = file.View.ReadInt32 (current_offset+0x0C),
                    FileCount       = file.View.ReadInt32 (current_offset+0x10),
                    FileNameLength  = file.View.ReadInt32 (current_offset+0x14),
                };
                sections.Add (section);
            }

            var list = new List<Entry> (count);
            foreach (var section in sections)
            {
                current_offset = section.IndexOffset;
                uint dir_size = (uint)(section.DirCount * section.DirNameLength);
                var dir_names = new byte[dir_size];
                if (dir_size != file.View.Read (current_offset, dir_names, 0, dir_size))
                    return null;
                current_offset += dir_size;
                DecryptNames (dir_names, section.DirNameLength);

                var dirs = new TcdDirEntry[section.DirCount];
                for (int i = 0; i < dirs.Length; ++i)
                {
                    dirs[i].FileCount   = file.View.ReadInt32 (current_offset);
                    dirs[i].NamesOffset = file.View.ReadInt32 (current_offset+4);
                    dirs[i].FirstIndex  = file.View.ReadInt32 (current_offset+8);
                    current_offset += 0x10;
                }

                uint entries_size = (uint)(section.FileCount * section.FileNameLength);
                var file_names = new byte[entries_size];
                if (entries_size != file.View.Read (current_offset, file_names, 0, entries_size))
                    return null;
                current_offset += entries_size;
                DecryptNames (file_names, section.FileNameLength);

                var offsets = new uint[section.FileCount + 1];
                for (int i = 0; i < offsets.Length; ++i)
                {
                    offsets[i] = file.View.ReadUInt32 (current_offset);
                    current_offset += 4;
                }

                int dir_name_offset = 0;
                foreach (var dir in dirs)
                {
                    string dir_name = Binary.GetCString (dir_names, dir_name_offset, section.DirNameLength);
                    dir_name_offset += section.DirNameLength;
                    int index = dir.FirstIndex;
                    int name_offset = dir.NamesOffset;
                    for (int i = 0; i < dir.FileCount; ++i)
                    {
                        string name = Binary.GetCString (file_names, name_offset, section.FileNameLength);
                        name_offset += section.FileNameLength;
                        name = dir_name + '\\' + name;
                        var entry = new TcdEntry (index, name, file, offsets[index]);
                        entry.Size = offsets[index+1] - offsets[index];
                        ++index;
                        list.Add (entry);
                    }
                }
            }
            return new TcdArchive (file, this, list);
        }

        private void DecryptNames (byte[] buffer, int name_length)
        {
            byte key = buffer[name_length-1];
            for (int i = 0; i < buffer.Length; ++i)
                buffer[i] -= key;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tcde = entry as TcdEntry;
            var tcda = arc as TcdArchive;
            if (null == tcde || null == tcda || entry.Size <= 0x14)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            int signature = arc.File.View.ReadInt32 (entry.Offset);
            if (0x43445053 == signature) // 'SPDC'
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (0x5367674F == signature) // 'OggS'
                return DecryptOgg (arc, entry);
            var header = new byte[0x14];
            arc.File.View.Read (entry.Offset, header, 0, 0x14);
            bool spdc_entry = false;
            if (null == tcda.Key)
            {
                foreach (var key in KnownKeys.Values)
                {
                    int first = signature + key * (tcde.Index + 3);
                    if (0x43445053 == first) // 'SPDC'
                    {
                        tcda.Key = key;
                        spdc_entry = true;
                        break;
                    }
                }
            }
            else if (0x43445053 == signature + tcda.Key.Value * (tcde.Index + 3))
            {
                spdc_entry = true;
            }
            if (spdc_entry && 0 != tcda.Key.Value)
            {
                unsafe
                {
                    fixed (byte* raw = header)
                    {
                        int* dw = (int*)raw;
                        for (int i = 0; i < 5; ++i)
                            dw[i] += tcda.Key.Value * (tcde.Index + 3 + i);
                    }
                }
            }
            var rest = arc.File.CreateStream (entry.Offset+0x14, entry.Size-0x14);
            return new PrefixStream (header, rest);
        }

        static Lazy<uint[]> OggCrcTable = new Lazy<uint[]> (InitOggCrcTable);

        static uint[] InitOggCrcTable ()
        {
            var table = new uint[0x100];
            for (uint i = 0; i < 0x100; ++i)
            {
                uint a = i << 24;
                for (int j = 0; j < 8; ++j)
                {
                    bool carry = 0 != (a & 0x80000000);
                    a <<= 1;
                    if (carry)
                        a ^= 0x04C11DB7;
                }
                table[i] = a;
            }
            return table;
        }

        Stream DecryptOgg (ArcFile arc, Entry entry)
        {
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            int remaining = data.Length;
            int src = 0;
            while (remaining > 0x1B && Binary.AsciiEqual (data, src, "OggS"))
            {
                int d = data[src+0x1A];
                data[src+0x16] = 0;
                data[src+0x17] = 0;
                data[src+0x18] = 0;
                data[src+0x19] = 0;
                int dst = src + 0x1B;
                int count = d + 0x1B;
                if (d != 0)
                {
                    if (remaining < count)
                        break;
                    for (int i = 0; i < d; ++i)
                        count += data[dst++];
                }
                remaining -= count;
                if (remaining < 0)
                    break;
                dst = src + 0x16;
                uint crc = 0;
                for (int i = 0; i < count; ++i)
                {
                    uint x = (crc >> 24) ^ data[src++];
                    crc <<= 8;
                    crc ^= OggCrcTable.Value[x];
                }
                LittleEndian.Pack (crc, data, dst);
            }
            return new MemoryStream (data);
        }
    }
}
