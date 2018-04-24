//! \file       ArcWSM.cs
//! \date       Sun Jan 29 05:30:28 2017
//! \brief      Music archive format by Tanaka Tatsuhiro.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class Wsm2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "WSM2"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine music archive v2"; } }
        public override uint     Signature { get { return 0x324D5357; } } // 'WSM2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Wsm2Opener ()
        {
            Extensions = new string[] { "wsm" };
            Signatures = new uint[] { 0x324D5357, 0x334D5357 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count) || index_size >= file.MaxOffset - 0x40)
                return null;
            int version = file.View.ReadByte (3) - '0';
            int table_offset = file.View.ReadInt32 (0x10);
            int table_count = file.View.ReadInt32 (0x14);
            if (table_offset >= index_size || !IsSaneCount (table_count))
                return null;
            var index = file.View.ReadBytes (0x40, index_size);
            var dir = new List<Entry> (count);
            for (int i = 0; i < table_count; ++i)
            {
                var entry = new Entry { Type = "audio" };
                entry.Offset = index.ToUInt32 (table_offset) - 0x14;
                entry.Size   = index.ToUInt32 (table_offset+8) + 0x14;
                if (3 == version)
                    entry.Size += index.ToUInt32 (table_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                table_offset += 0x20;
                dir.Add (entry);
            }
            int index_offset = 0;
            for (int i = 0; i < count; ++i)
            {
                int entry_pos = index.ToInt32 (index_offset);
                index_offset += 4;
                int name_length = index[entry_pos+1];
                var name = Binary.GetCString (index, entry_pos+2, name_length-2);
                if (0 == name.Length)
                    return null;
                entry_pos += name_length;
                int entry_idx = index[entry_pos+3];
                if (entry_idx >= dir.Count)
                    return null;
                var entry = dir[entry_idx];
                entry.Name = string.Format ("{0:D2}_{1}.wav", entry_idx, name);
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Wsm0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "WSM0"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine music archive v0"; } }
        public override uint     Signature { get { return 0x304D5357; } } // 'WSM0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Wsm0Opener ()
        {
            Extensions = new string[] { "wsm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count) || index_size >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0, index_size);
            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                int entry_pos = index.ToInt32 (index_offset);
                index_offset += 4;
                int name_length = index[entry_pos];
                var name = Binary.GetCString (index, entry_pos+1, name_length-1);
                if (0 == name.Length)
                    return null;
                entry_pos += name_length;
                var entry = new WsmEntry {
                    Name = string.Format ("{0}.wav", name),
                    Type = "audio",
                    Offset = index.ToUInt32 (entry_pos+8),
                    Size   = index.ToUInt32 (entry_pos+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Format.FormatTag = 1;
                entry.Format.Channels = 2;
                entry.Format.BitsPerSample = 16;
                entry.Format.SamplesPerSecond = 44100;
                entry.Format.BlockAlign = (ushort)(entry.Format.Channels * entry.Format.BitsPerSample/8);
                entry.Format.AverageBytesPerSecond = entry.Format.SamplesPerSecond * entry.Format.BlockAlign;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = (WsmEntry)entry;
            using (var riff = new MemoryStream (0x2C))
            {
                WaveAudio.WriteRiffHeader (riff, went.Format, entry.Size);
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new PrefixStream (riff.ToArray(), input);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Wsm1Opener : Wsm0Opener
    {
        public override string         Tag { get { return "WSM1"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine music archive v1"; } }
        public override uint     Signature { get { return 0x314D5357; } } // 'WSM1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Wsm1Opener ()
        {
            Extensions = new string[] { "wsm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count) || index_size >= file.MaxOffset)
                return null;
            var index = file.View.ReadBytes (0, index_size);
            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                int entry_pos = index.ToInt32 (index_offset);
                index_offset += 4;
                int name_length = index[entry_pos];
                var name = Binary.GetCString (index, entry_pos+1, name_length-1);
                if (0 == name.Length)
                    return null;
                entry_pos += name_length;
                var entry = new WsmEntry {
                    Name = string.Format ("{0}.wav", name),
                    Type = "audio",
                    Offset = index.ToUInt32 (entry_pos+8),
                    Size   = index.ToUInt32 (entry_pos+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Format.FormatTag = 1;
                entry.Format.Channels = index[entry_pos+2];
                entry.Format.BitsPerSample = index[entry_pos+3];
                entry.Format.SamplesPerSecond = index.ToUInt32 (entry_pos+4);
                entry.Format.BlockAlign = (ushort)(entry.Format.Channels * entry.Format.BitsPerSample/8);
                entry.Format.AverageBytesPerSecond = entry.Format.SamplesPerSecond * entry.Format.BlockAlign;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class WsmEntry : Entry
    {
        public WaveFormat Format;
    }

    [Export(typeof(ArchiveFormat))]
    public class Wsm4Opener : ArchiveFormat
    {
        public override string         Tag { get { return "WSM4"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine music archive v4"; } }
        public override uint     Signature { get { return 0x344D5357; } } // 'WSM4'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Wsm4Opener ()
        {
            Extensions = new string[] { "wsm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint data_offset = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count) || data_offset >= file.MaxOffset)
                return null;
            int table_offset = file.View.ReadInt32 (0x10);
            int table_count = file.View.ReadInt32 (0x14);
            if (table_offset >= data_offset || !IsSaneCount (table_count))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < table_count; ++i)
            {
                var entry = new Entry { Name = i.ToString ("D4"), Type = "audio" };
                entry.Offset = file.View.ReadUInt32 (table_offset);
                entry.Size   = file.View.ReadUInt32 (table_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                table_offset += 0x24;
                dir.Add (entry);
            }
            int index_offset = 0x44;
            for (int i = 0; i < count; ++i)
            {
                dir[i].Name = file.View.ReadString (index_offset, 0x40);
                index_offset += 0x1A8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
