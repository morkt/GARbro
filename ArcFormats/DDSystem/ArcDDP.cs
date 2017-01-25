//! \file       ArcDDP.cs
//! \date       Wed Jul 27 13:21:01 2016
//! \brief      DDSystem resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Formats.SHSystem;
using GameRes.Utility;

namespace GameRes.Formats.DDSystem
{
    [Export(typeof(ArchiveFormat))]
    public class Ddp2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DDP2"; } }
        public override string Description { get { return "DDSystem engine resource archive"; } }
        public override uint     Signature { get { return 0x32504444; } } // 'DDP2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Ddp2Opener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x20;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry {
                    Offset      = file.View.ReadUInt32 (index_offset),
                    UnpackedSize = file.View.ReadUInt32 (index_offset+4),
                    Size        = file.View.ReadUInt32 (index_offset+8),
                    Name        = string.Format ("{0}#{1:D5}", base_name, i),
                };
                entry.IsPacked = entry.Size != 0;
                if (!entry.IsPacked)
                    entry.Size = entry.UnpackedSize;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x10;
                dir.Add (entry);
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }

        internal static void DetectFileTypes (ArcView file, List<Entry> dir)
        {
            byte[] signature_buffer = new byte[4];
            foreach (PackedEntry entry in dir)
            {
                uint signature;
                if (entry.IsPacked)
                {
                    using (var input = file.CreateStream (entry.Offset, Math.Min (entry.Size, 0x20u)))
                    using (var reader = new ShsCompression (input))
                    {
                        reader.Unpack (signature_buffer);
                        signature = LittleEndian.ToUInt32 (signature_buffer, 0);
                    }
                }
                else
                {
                    signature = file.View.ReadUInt32 (entry.Offset);
                }
                if (0x78534444 == signature) // 'DDSx'
                {
                    entry.Type = "script";
                    entry.Name = Path.ChangeExtension (entry.Name, "hxb");
                }
                else if (0 != signature)
                {
                    IResource res;
                    if (0x020000 == signature || 0x0A0000 == signature)
                        res = ImageFormat.Tga;
                    else
                        res = AutoEntry.DetectFileType (signature);
                    if (res != null)
                        entry.ChangeType (res);
                }
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            var data = new byte[pent.UnpackedSize];
            using (var reader = new ShsCompression (input))
            {
                reader.Unpack (data);
                if (data.Length > 16 && Binary.AsciiEqual (data, 0, "DDSxHXB"))
                    DecryptHxb (data);
                return new BinMemoryStream (data, entry.Name);
            }
        }

        internal void DecryptHxb (byte[] data)
        {
            int length = data[8] << 16 | data[9] << 8 | data[10];
            if (length != data.Length)
                return;
            int key = (((length << 5) ^ 0xA5) * (length + 0x6F349)) ^ 0x34A9B129;
            var key_bits = new byte[4];
            LittleEndian.Pack (key, key_bits, 0);
            for (int i = 0x10; i < data.Length; ++i)
            {
                data[i] ^= key_bits[i & 3];
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class Ddp3Opener : Ddp2Opener
    {
        public override string         Tag { get { return "DDP3"; } }
        public override string Description { get { return "DDSystem engine resource archive"; } }
        public override uint     Signature { get { return 0x33504444; } } // 'DDP3'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var index = Him5Opener.ReadIndex (file, 0x20, count);
            var dir = new List<Entry>();
            foreach (var section in index)
            {
                int index_offset = section.Item1;
                for (int section_size = section.Item2; section_size > 0; )
                {
                    int entry_size = file.View.ReadByte (index_offset);
                    if (entry_size < 17)
                        break;
                    var entry = new PackedEntry {
                        Offset = file.View.ReadUInt32 (index_offset+1),
                        UnpackedSize = file.View.ReadUInt32 (index_offset+5),
                        Size = file.View.ReadUInt32 (index_offset+9),
                        Name = file.View.ReadString (index_offset+17, (uint)entry_size-17),
                    };
                    entry.IsPacked = entry.Size != 0;
                    if (!entry.IsPacked)
                        entry.Size = entry.UnpackedSize;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    index_offset += entry_size;
                    section_size -= entry_size;
                    dir.Add (entry);
                }
            }
            DetectFileTypes (file, dir);
            return new ArcFile (file, this, dir);
        }
    }
}
