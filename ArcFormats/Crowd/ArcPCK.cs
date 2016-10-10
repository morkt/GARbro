//! \file       ArcPCK.cs
//! \date       Thu Jun 11 12:58:00 2015
//! \brief      Crowd engine resource archive.
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

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK"; } }
        public override string Description { get { return "Crowd engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfffff)
                return null;
            long index_offset = 4;
            uint index_size = (uint)(0xc * count);
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry>();
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Offset = file.View.ReadUInt32 (index_offset+4),
                    Size   = file.View.ReadUInt32 (index_offset+8)
                };
                if (entry.Offset < index_size || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            byte[] name_buf = new byte[260];
            foreach (var entry in dir)
            {
                uint max_len = Math.Min (260u, file.View.Reserve (index_offset, 260));
                uint n;
                for (n = 0; n < max_len; ++n)
                {
                    byte b = file.View.ReadByte (index_offset+n);
                    if (0 == b)
                        break;
                    name_buf[n] = b;
                }
                if (0 == n || max_len == n)
                    return null;
                entry.Name = Encodings.cp932.GetString (name_buf, 0, (int)n);
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                index_offset += n+1;
            }
            return new ArcFile (file, this, dir);
        }
    }

    internal class PkwEntry : PackedEntry
    {
        public WaveFormat   Format;
    }

    [Export(typeof(ArchiveFormat))]
    public class PkwOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PKWV"; } }
        public override string Description { get { return "Crowd engine audio archive"; } }
        public override uint     Signature { get { return 0x56574b50; } } // 'PKWV'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PkwOpener ()
        {
            Extensions = new string[] { "PCK" };
        }

        const uint WaveHeaderSize = 8/*RIFF*/ + 12/*WAVEfmt*/ + 0x10/*fmt*/ + 8/*data*/;

        public override ArcFile TryOpen (ArcView file)
        {
            int format_count = file.View.ReadUInt16 (4);
            int count = file.View.ReadUInt16 (6);
            if (0 == format_count || 0 == count)
                return null;
            uint index_offset = 8;
            long base_offset = index_offset + format_count*0x14 + count*0x18;
            if (base_offset >= file.MaxOffset)
                return null;
            var formats = new List<WaveFormat> (format_count);
            for (int i = 0; i < format_count; ++i)
            {
                var format = new WaveFormat
                {
                    FormatTag               = file.View.ReadUInt16 (index_offset),
                    Channels                = file.View.ReadUInt16 (index_offset+2),
                    SamplesPerSecond        = file.View.ReadUInt32 (index_offset+4),
                    AverageBytesPerSecond   = file.View.ReadUInt32 (index_offset+8),
                    BitsPerSample           = file.View.ReadUInt16 (index_offset+12),
                    BlockAlign              = file.View.ReadUInt16 (index_offset+14),
                };
                index_offset += 0x14;
                formats.Add (format);
            }
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int fmt_index = file.View.ReadUInt16 (index_offset);
                if (fmt_index > formats.Count)
                    return null;
                string name = file.View.ReadString (index_offset+2, 0x0A);
                var entry = new PkwEntry
                {
                    Name = name + ".wav",
                    Type = "audio",
                    Offset = base_offset + file.View.ReadInt64 (index_offset+0x10),
                    Size = file.View.ReadUInt32 (index_offset+0x0C),
                    IsPacked = true,
                    Format = formats[fmt_index],
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.UnpackedSize = entry.Size + WaveHeaderSize;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = entry as PkwEntry;
            if (null == went)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var riff = new byte[0x2c];
            using (var buf = new MemoryStream (riff))
            using (var wav = new BinaryWriter (buf))
            {
                wav.Write (0x46464952); // 'RIFF'
                uint total_size = went.UnpackedSize - 8;
                wav.Write (total_size);
                wav.Write (0x45564157); // 'WAVE'
                wav.Write (0x20746d66); // 'fmt '
                wav.Write (0x10);
                wav.Write (went.Format.FormatTag);
                wav.Write (went.Format.Channels);
                wav.Write (went.Format.SamplesPerSecond);
                wav.Write (went.Format.AverageBytesPerSecond);
                wav.Write (went.Format.BlockAlign);
                wav.Write (went.Format.BitsPerSample);
                wav.Write (0x61746164); // 'data'
                wav.Write (went.Size);
                wav.Flush ();
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new PrefixStream (riff, input);
            }
        }
    }
}
