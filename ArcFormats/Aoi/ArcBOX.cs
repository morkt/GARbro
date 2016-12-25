//! \file       ArcBOX.cs
//! \date       Sun Jan 17 14:45:34 2016
//! \brief      Aoi engine event script archive.
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
using System.Security.Cryptography;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Aoi
{
    internal class BoxArchive : ArcFile
    {
        public readonly byte Key;

        public BoxArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class BoxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BOX"; } }
        public override string Description { get { return "Aoi engine script archive"; } }
        public override uint     Signature { get { return 0x42494F41; } } // 'AOIB'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<int, byte> VersionKeyMap = new Dictionary<int, byte> {
            {  4, 0xAD },
            {  5, 0xAD },
            {  6, 0xB4 },
            {  7, 0xB4 },
            { 10, 0xB2 },
            { 12, 0xA5 },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int version;
            if (file.View.AsciiEqual (4, "X10"))
                version = 10;
            else if (file.View.AsciiEqual (4, "X12"))
                version = 12;
            else if (file.View.AsciiEqual (4, "OX7\0"))
                version = 7;
            else if (file.View.AsciiEqual (4, "OX6\0"))
                version = 6;
            else if (file.View.AsciiEqual (4, "OX5 "))
                version = 5;
            else if (file.View.AsciiEqual (4, "OX4 "))
                version = 4;
            else
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            List<Entry> dir;
            if (version > 5)
                dir = ReadIndexV6 (file, count);
            else
                dir = ReadIndexV5 (file, count);
            if (null == dir)
                return null;
            return new BoxArchive (file, this, dir, VersionKeyMap[version]);
        }

        List<Entry> ReadIndexV6 (ArcView file, int count)
        {
            int index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = file.View.ReadString (index_offset, 0x10),
                    Type = "script",
                    Offset = file.View.ReadUInt32 (index_offset+0x10),
                    Size = file.View.ReadUInt32 (index_offset+0x14),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return dir;
        }

        List<Entry> ReadIndexV5 (ArcView file, int count)
        {
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            int index_offset = 0xC;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D2}.evt", base_name, i),
                    Type = "script",
                    Offset = next_offset,
                };
                next_offset = i+1 == count ? (uint)file.MaxOffset : file.View.ReadUInt32 (index_offset);
                entry.Size = next_offset - (uint)entry.Offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return dir;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var barc = arc as BoxArchive;
            if (null == barc)
                return input;
            return new XoredStream (input, barc.Key);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AoiMyOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AOIMY"; } }
        public override string Description { get { return "Aoi engine script archive"; } }
        public override uint     Signature { get { return 0x4D494F41; } } // 'AOIM'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AoiMyOpener ()
        {
            Extensions = new string[] { "box" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "Y01\0"))
                return null;
            int count = Binary.BigEndian (file.View.ReadInt32 (8));
            if (!IsSaneCount (count))
                return null;
            int index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = file.View.ReadString (index_offset, 0x10),
                    Type = "script",
                    Offset = Binary.BigEndian (file.View.ReadUInt32 (index_offset+0x10)),
                    Size = Binary.BigEndian (file.View.ReadUInt32 (index_offset+0x14)),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            uint offset = (uint)entry.Offset;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= KeyFromOffset (offset++);
            }
            return new BinMemoryStream (data, entry.Name);
        }

        static byte KeyFromOffset (uint offset)
        {
            uint v1 = offset - 0x5CC8E9D7u + (0xA3371629u >> (int)((offset & 0xF) + 1)) - (0x5CC8E9D7u << (int)(31 - (offset & 0xF)));

            uint v3 = v1 << (int)(31 - ((offset >> 4) & 0xF));
            uint v4 = v1 >> (int)(((offset >> 4) & 0xF) + 1);
            uint v5 = offset - 0x5CC8E9D7
                + ((offset - 0x5CC8E9D7u + v3 + v4) << (int)(31 - ((offset >> 8) & 0xF)))
                + ((offset - 0x5CC8E9D7u + v3 + v4) >> (int)(((offset >> 8) & 0xF) + 1));
            uint v6 = offset - 0x5CC8E9D7u
                + (v5 << (int)(31 - ((offset >> 12) & 0xF))) + (v5 >> (int)(((offset >> 12) & 0xF) + 1));
            uint v7 = offset - 0x5CC8E9D7u
                + (v6 << (int)(31 - ((offset >> 16) & 0xF))) + (v6 >> (int)(((offset >> 16) & 0xF) + 1));
            int v8 = (int)(offset >> 20) & 0xF;
            uint v9 = offset - 0x5CC8E9D7u
                + ((offset - 0x5CC8E9D7u + (v7 << (31 - v8)) + (v7 >> (v8 + 1))) << (int)(31 - ((offset >> 24) & 0xF)))
                + ((offset - 0x5CC8E9D7u + (v7 << (31 - v8)) + (v7 >> (v8 + 1))) >> (int)(((offset >> 24) & 0xF) + 1));
            uint key = (offset - 0x5CC8E9D7 + (v9 << (int)(31 - (offset >> 28))) + (v9 >> (int)((offset >> 28) + 1))) >> (int)(offset & 0xF);
            return (byte)key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AoiMyUnicodeOpener : AoiMyOpener
    {
        public override string         Tag { get { return "AOIMY/UNICODE"; } }
        public override string Description { get { return "Aoi engine script archive"; } }
        public override uint     Signature { get { return 0x004F0041; } } // 'A O '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var name_buffer = new byte[0x20];
            file.View.Read (0, name_buffer, 0, 0x16);
            if ("AOIMY01\0" != Encoding.Unicode.GetString (name_buffer, 0, 0x10))
                return null;
            int count = Binary.BigEndian (file.View.ReadInt32 (0x10));
            if (!IsSaneCount (count))
                return null;
            int index_offset = 0x18;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (0x20 != file.View.Read (index_offset, name_buffer, 0, 0x20))
                    return null;
                int n;
                for (n = 0; n < name_buffer.Length; n += 2)
                    if (0 == name_buffer[n] && 0 == name_buffer[n+1])
                        break;
                if (0 == n)
                    return null;
                var entry = new Entry {
                    Name = Encoding.Unicode.GetString (name_buffer, 0, n),
                    Type = "script",
                    Offset = Binary.BigEndian (file.View.ReadUInt32 (index_offset+0x20)),
                    Size = Binary.BigEndian (file.View.ReadUInt32 (index_offset+0x24)),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
