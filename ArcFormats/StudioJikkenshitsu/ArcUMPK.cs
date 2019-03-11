//! \file       ArcUMPK.cs
//! \date       2019 Mar 10
//! \brief      UM utility library audio archive.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Umut
{
    internal class UmEntry : Entry
    {
        public byte Key;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/UMPK"; } }
        public override string Description { get { return "UM Utility engine audio archive"; } }
        public override uint     Signature { get { return 0x4B504D55; } } // 'UMPK'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "0001"))
                return null;
            uint name_length = file.View.ReadByte (0x18);
            uint base_offset = 0x1A + name_length;
            if (file.View.ReadUInt32 (base_offset) != 0)
                return null;
            int count = file.View.ReadInt32 (base_offset+8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = base_offset + 12;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint rec_length = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                uint offset = file.View.ReadUInt32 (index_offset+8);
                uint id     = file.View.ReadUInt32 (index_offset+12);
                name_length = file.View.ReadUInt32 (index_offset+16);
                var name = file.View.ReadString (index_offset+20, name_length);
                var entry = Create<UmEntry> (name);
                entry.Offset = 8L + base_offset + offset;
                entry.Size   = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Key = GetEntryKey (name, size, id);
                dir.Add (entry);
                index_offset += rec_length + 4;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var ument = entry as UmEntry;
            if (null == ument)
                return input;
            return new XoredStream (input, ument.Key);
        }

        internal static byte GetEntryKey (string name, uint size, uint id)
        {
            uint name_sum = 0;
            for (int i = 0; i < name.Length; ++i)
            {
                name_sum += (uint)name[i];
            }
            uint key = size + id;
            key += name_sum + (key >> 8) + (key >> 16) + (key >> 24);
            key &= 0xFF;
            if (0 == key)
                key = 0x37;
            return (byte)key;
        }
    }
}
