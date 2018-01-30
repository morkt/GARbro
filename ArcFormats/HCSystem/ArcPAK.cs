//! \file       ArcPAK.cs
//! \date       2018 Jan 30
//! \brief      hcsystem resource archive.
//
// Copyright (C) 2018 by morkt
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
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.HCSystem
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/HCSYSTEM"; } }
        public override string Description { get { return "hcsystem engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            bool is_encrypted = file.View.ReadByte (8) == 1;
            bool is_unicode = false;
            uint entry_size = 0x2C;
            if (!CheckFirstOffset (file, count, entry_size, is_encrypted))
            {
                entry_size = 0x4C;
                if (!CheckFirstOffset (file, count, entry_size, is_encrypted))
                    return null;
                is_unicode = true;
            }
            var index = file.View.ReadBytes (0xC, entry_size * (uint)count);
            if (is_encrypted)
                DecryptIndex (index);

            int name_size = is_unicode ? 0x40 : 0x20;
            Func<int, string> read_name;
            if (is_unicode)
            {
                read_name = pos => {
                    int len = 0;
                    for (; len < name_size; len += 2)
                    {
                        if (index[pos+len] == 0 && index[pos+len+1] == 0)
                            break;
                    }
                    return Encoding.Unicode.GetString (index, pos, len);
                };
            }
            else
                read_name = pos => Binary.GetCString (index, pos, name_size);

            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = read_name (index_offset);
                index_offset += name_size;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.UnpackedSize = index.ToUInt32 (index_offset);
                entry.Size         = index.ToUInt32 (index_offset+4);
                entry.Offset       = index.ToUInt32 (index_offset+8);
                entry.IsPacked = entry.Size != 0;
                if (!entry.IsPacked)
                    entry.Size = entry.UnpackedSize;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LzssStream (input);
        }

        private bool CheckFirstOffset (ArcView file, int count, uint entry_size, bool is_encrypted)
        {
            uint data_offset = 0xC + entry_size * (uint)count;
            uint first_offset = file.View.ReadUInt32 (0xC + entry_size - 4);
            if (is_encrypted)
                first_offset = (first_offset >> 4) & 0x0F0F0F0F | (first_offset << 4) & 0xF0F0F0F0;
            return first_offset == data_offset;
        }

        void DecryptIndex (byte[] data)
        {
            for (int i = 0; i < data.Length; ++i)
                data[i] = Binary.RotByteL (data[i], 4);
        }
    }
}
