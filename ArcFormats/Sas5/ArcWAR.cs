//! \file       ArcWAR.cs
//! \date       Fri Oct 23 19:11:56 2015
//! \brief      Sas5 engine audio archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Sas5
{
    internal class WarEntry : Entry
    {
        public int  Format;
    }

    [Export(typeof(ArchiveFormat))]
    public class WarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "WAR/SAS5"; } }
        public override string Description { get { return "SAS5 engine audio archive"; } }
        public override uint     Signature { get { return 0x20726177; } } // 'war '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public WarOpener ()
        {
            Extensions = new string[] { "war" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint entry_size = file.View.ReadUInt32 (12);
            if (entry_size < 0x18)
                return null;
            var GetEntryName = CreateEntryNameDelegate (file.Name);

            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new WarEntry {
                    Name    = GetEntryName (i),
                    Offset  = file.View.ReadUInt32 (index_offset),
                    Size    = file.View.ReadUInt32 (index_offset+4),
                    Format  = file.View.ReadByte (index_offset+0x14),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (0 == entry.Format)
                {
                    entry.Name = Path.ChangeExtension (entry.Name, "wav");
                    entry.Type = "audio";
                }
                else if (2 == entry.Format)
                {
                    entry.Name = Path.ChangeExtension (entry.Name, "ogg");
                    entry.Type = "audio";
                }
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = entry as WarEntry;
            if (null == went || 0 != went.Format)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            return OpenWavEntry (arc.File, entry.Offset, entry.Size);
        }

        internal Func<int, string> CreateEntryNameDelegate (string arc_name)
        {
            var index = Sec5Opener.LookupIndex (arc_name);
            string base_name = Path.GetFileNameWithoutExtension (arc_name);
            if (null == index)
                return n => GetDefaultName (base_name, n);
            else
                return (n) => {
                    Entry entry;
                    if (index.TryGetValue (n, out entry))
                        return entry.Name;
                    return GetDefaultName (base_name, n);
                };
        }

        internal static string GetDefaultName (string base_name, int n)
        {
            return string.Format ("{0}#{1:D5}", base_name, n);
        }

        internal Stream OpenWavEntry (ArcView file, long offset, uint size)
        {
            uint fmt_size = file.View.ReadUInt32 (offset);
            uint data_size = file.View.ReadUInt32 (offset+4);
            var wav_header = new byte[8+12+fmt_size+8];
            uint total_size = (uint)wav_header.Length + data_size - 8;
            file.View.Read (offset+8, wav_header, 0x14, fmt_size);
            using (var mem = new MemoryStream (wav_header))
            using (var buffer = new BinaryWriter (mem))
            {
                buffer.Write (AudioFormat.Wav.Signature);
                buffer.Write (total_size);
                buffer.Write (0x45564157); // 'WAVE'
                buffer.Write (0x20746d66); // 'fmt '
                buffer.Write (fmt_size);
                buffer.BaseStream.Seek (fmt_size, SeekOrigin.Current);
                buffer.Write (0x61746164); // 'data'
                buffer.Write (data_size);
            }
            var pcm_data = file.CreateStream (offset+8+fmt_size, size-8-fmt_size);
            return new PrefixStream (wav_header, pcm_data);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class War2Opener : WarOpener
    {
        public override string         Tag { get { return "WAR2/SAS5"; } }
        public override string Description { get { return "SAS5 engine audio archive"; } }
        public override uint     Signature { get { return 0x32726177; } } // 'war2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public War2Opener ()
        {
            Extensions = new string[] { "war" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = file.View.ReadUInt32 (4);
            if (header_size >= file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (12);
            if (!IsSaneCount (count))
                return null;
            var GetEntryName = CreateEntryNameDelegate (file.Name);

            uint index_offset = header_size+8;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                index_offset += 4;
                var entry = new Entry {
                    Name    = GetEntryName (i),
                    Offset  = next_offset,
                    Type    = "audio",
                };
                next_offset = file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var offset = entry.Offset;
            var end_offset = entry.Offset + entry.Size;
            byte id = arc.File.View.ReadByte (offset++);
            if (0 != (id & 0x40))
                offset += 12;
            if (offset >= end_offset)
                return Stream.Null;
            uint size = (uint)(end_offset - offset);
            int format = id & 0xF;
            if (0 == format)
                return OpenWavEntry (arc.File, offset, size);
            return arc.File.CreateStream (offset, size);
        }
    }
}
