//! \file       ArcGsPack.cs
//! \date       Thu Apr 16 14:55:01 2015
//! \brief      GsPack resource archives implementation.
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

namespace GameRes.Formats.Gs
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GsPack"; } }
        public override string Description { get { return "GsPack resource archive"; } }
        public override uint     Signature { get { return 0x61746144; } } // 'Data'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak", "dat", "pa_" };
            Signatures = new   uint[] { 0x61746144, 0x61507347 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!(file.View.AsciiEqual (0, "DataPack5") || 
                  file.View.AsciiEqual (0, "GsPack5") ||
                  file.View.AsciiEqual (0, "GsPack4")))
                return null;
            int version_minor = file.View.ReadUInt16 (0x30);
            int version_major = file.View.ReadUInt16 (0x32);
            uint index_size = file.View.ReadUInt32 (0x34);
            int count = file.View.ReadInt32 (0x3c);
            if (count <= 0 || count > 0xfffff || index_size > 0xffffff)
                return null;
            uint crypt_key = file.View.ReadUInt32 (0x38);
            long data_offset = file.View.ReadUInt32 (0x40);
            uint index_offset = file.View.ReadUInt32 (0x44);
            int entry_size = version_major < 5 ? 0x48 : 0x68;
            int unpacked_size = count * entry_size;
            byte[] packed_index = new byte[index_size];
            if (index_size != file.View.Read (index_offset, packed_index, 0, index_size))
                return null;
            if (0 != crypt_key)
                for (int i = 0; i != packed_index.Length; ++i)
                    packed_index[i] ^= (byte)(i & crypt_key);
            using (var stream = new MemoryStream (packed_index))
            using (var reader = new LzssReader (stream, packed_index.Length, unpacked_size))
            {
                reader.Unpack();
                var index = reader.Data;
                index_offset = 0;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    string name = Binary.GetCString (index, (int)index_offset, 0x40);
                    if (0 != name.Length)
                    {
                        long offset = data_offset + LittleEndian.ToUInt32 (index, (int)index_offset+0x40);
                        var entry = AutoEntry.Create (file, offset, name);
                        entry.Size = LittleEndian.ToUInt32 (index, (int)index_offset+0x44);
                        dir.Add (entry);
                    }
                    index_offset += (uint)entry_size;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GsData"; } }
        public override string Description { get { return "GsPack resource archive"; } }
        public override uint     Signature { get { return 0x59537347; } } // 'GsSY'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "GsSYMBOL5BINDATA"))
                return null;
            uint header_size = file.View.ReadUInt32 (0xa4);
            if (header_size < 0xd0)
                return null;
            int count = file.View.ReadInt32 (0xa8);
            uint index_offset = file.View.ReadUInt32 (0xb8);
            uint index_size = file.View.ReadUInt32 (0xbc);
            uint crypt_key = file.View.ReadUInt32 (0xc0);
            uint unpacked_index_size = file.View.ReadUInt32 (0xc4);
            if (count * 0x18 != unpacked_index_size)
                return null;
            uint data_offset = file.View.ReadUInt32 (0xc8);
            byte[] packed_index = new byte[index_size];
            if (index_size != file.View.Read (index_offset, packed_index, 0, index_size))
                return null;
            if (0 != crypt_key)
                for (int i = 0; i != packed_index.Length; ++i)
                    packed_index[i] ^= (byte)(i & crypt_key);
            using (var stream = new MemoryStream (packed_index))
            using (var reader = new LzssReader (stream, packed_index.Length, (int)unpacked_index_size))
            {
                reader.Unpack();
                var index = reader.Data;
                index_offset = 0;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PackedEntry
                    {
                        Name = i.ToString ("D5"),
                        Offset = data_offset + LittleEndian.ToUInt32 (index, (int)index_offset),
                        Size = LittleEndian.ToUInt32 (index, (int)index_offset + 4),
                        UnpackedSize = LittleEndian.ToUInt32 (index, (int)index_offset + 8),
                    };
                    dir.Add (entry);
                    index_offset += 0x18;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed_entry = entry as PackedEntry;
            if (null == packed_entry)
                return input;
            using (input)
            using (var reader = new LzssReader (input, (int)packed_entry.Size, (int)packed_entry.UnpackedSize))
            {
                reader.Unpack();
                return new MemoryStream (reader.Data, false);
            }
        }
    }
}
