//! \file       ArcMGD.cs
//! \date       Sun May 10 18:11:01 2015
//! \brief      MEGU archives implementation.
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

// MEGU
// Masys Enhanced Game Unit

namespace GameRes.Formats.Megu
{
    [Export(typeof(ArchiveFormat))]
    public class MgdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGD"; } }
        public override string Description { get { return "Masys resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        internal static readonly string Key = "Powerd by Masys";

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            if (0x44474d != (signature & 0xffffff)) // 'MGD'
                return null;
            int count = file.View.ReadInt16 (0x20);
            if (count <= 0)
                return null;
            int flag = file.View.ReadUInt16 (3);
            var dir = new List<Entry> (count);
            int index_offset = 0x22;
            byte[] name_buf = new byte[16];
            for (uint i = 0; i < count; ++i)
            {
                int name_size = file.View.ReadByte (index_offset+1);
                if (0 == name_size)
                    return null;
                if (name_size > name_buf.Length)
                    Array.Resize (ref name_buf, name_size);
                file.View.Read (index_offset+2, name_buf, 0, (uint)name_size);
                if (100 == flag)
                    Decrypt (name_buf, 0, name_size);
                string name = Encodings.cp932.GetString (name_buf, 0, name_size);
                index_offset += 2 + name_size;

                uint offset = file.View.ReadUInt32 (index_offset+4);
                var entry = AutoEntry.Create (file, offset, name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        internal static void Decrypt (byte[] buffer, int offset, int length)
        {
            for (int i = 0; i < length; ++i)
            {
                buffer[offset+i] ^= (byte)Key[i%0xf];
            }
        }
    }

    internal class MgsEntry : Entry
    {
        public ushort  Channels;
        public uint    SamplesPerSecond;
        public ushort  BitsPerSample;
        public byte    Format;
    }

    [Export(typeof(ArchiveFormat))]
    public class MgsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MGS"; } }
        public override string Description { get { return "Masys audio resources archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            if (0x53474d != (signature & 0xffffff)) // 'MGS'
                return null;
            int count = file.View.ReadInt16 (0x20);
            if (count <= 0)
                return null;
            int flag = file.View.ReadUInt16 (3);
            var dir = new List<Entry> (count);
            int index_offset = 0x22;
            byte[] name_buf = new byte[16];
            for (int i = 0; i < count; ++i)
            {
                byte format = file.View.ReadByte (index_offset);
                int name_size = file.View.ReadByte (index_offset+9);
                if (0 == name_size)
                    return null;
                if (name_size > name_buf.Length)
                    Array.Resize (ref name_buf, name_size);
                file.View.Read (index_offset+10, name_buf, 0, (uint)name_size);
                if (100 == flag)
                    MgdOpener.Decrypt (name_buf, 0, name_size);
                var name = Encodings.cp932.GetString (name_buf, 0, name_size);
                name = Path.ChangeExtension (name, GetExtFromFormatId (format));

                var entry = FormatCatalog.Instance.Create<MgsEntry> (name);
                entry.Format = format;
                if (0 == format)
                {
                    entry.Channels = file.View.ReadUInt16 (index_offset+1);
                    entry.SamplesPerSecond = file.View.ReadUInt32 (index_offset+3);
                    entry.BitsPerSample = file.View.ReadUInt16 (index_offset+7);
                }
                index_offset += 10 + name_size;
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var went = entry as MgsEntry;
            if (null == went || went.Format != 0)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            using (var riff = new MemoryStream (0x2C))
            {
                ushort align = (ushort)(went.Channels * went.BitsPerSample / 8);
                var format = new WaveFormat {
                    FormatTag = 1,
                    Channels = went.Channels,
                    SamplesPerSecond = went.SamplesPerSecond,
                    AverageBytesPerSecond = went.SamplesPerSecond * align,
                    BlockAlign = align,
                    BitsPerSample = went.BitsPerSample,
                };
                WaveAudio.WriteRiffHeader (riff, format, entry.Size);
                var input = arc.File.CreateStream (entry.Offset, entry.Size);
                return new PrefixStream (riff.ToArray(), input);
            }
        }

        internal static string GetExtFromFormatId (int id)
        {
            switch (id)
            {
            case 0: return "wav";
            case 1: return "mid";
            default: return null;
            }
        }
    }
}
