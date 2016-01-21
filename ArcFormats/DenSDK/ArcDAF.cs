//! \file       ArcDAF.cs
//! \date       Thu Jan 21 22:33:29 2016
//! \brief      DenSDK engine resource archive.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.DenSdk
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/DenSDK"; } }
        public override string Description { get { return "DenSDK resource archive"; } }
        public override uint     Signature { get { return 0x32464144; } } // 'DAF2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = (uint)(file.View.ReadByte (0x20) << 24
                            | file.View.ReadByte (0x25) << 16
                            | file.View.ReadByte (0x2A) << 8
                            | file.View.ReadByte (0x2F));
            int count = file.View.ReadInt32 (8) ^ (int)key;
            if (!IsSaneCount (count))
                return null;
            uint packed_size = file.View.ReadUInt32 (0x10) ^ key;
            uint unpacked_size = file.View.ReadUInt32 (0x14) ^ key;
            uint base_offset = file.View.ReadUInt32 (0x1C) ^ key;
            byte[] index = new byte[unpacked_size];
            bool is_packed = file.View.ReadInt32 (0x18) == 1;
            if (is_packed)
            {
                using (var input = file.CreateStream (0x30, packed_size))
                using (var zindex = new ZLibStream (input, CompressionMode.Decompress))
                    zindex.Read (index, 0, index.Length);
                base_offset = 0x30 + packed_size;
            }
            else
            {
                file.View.Read (0x30, index, 0, unpacked_size);
            }
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int entry_size = LittleEndian.ToInt32 (index, index_offset) ^ (int)key;
                if (entry_size < 0x30 || entry_size > index.Length-index_offset)
                    return null;
                var name = Binary.GetCString (index, index_offset+0x34, entry_size-0x34);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset        = base_offset + (LittleEndian.ToUInt32 (index, index_offset+4) ^ (uint)key);
                entry.Size          = LittleEndian.ToUInt32 (index, index_offset+8) ^ (uint)key;
                entry.UnpackedSize  = LittleEndian.ToUInt32 (index, index_offset+0xC) ^ (uint)key;
                entry.IsPacked      = LittleEndian.ToInt32 (index, index_offset+0x30) != 0;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
