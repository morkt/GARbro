//! \file       ArcAVC.cs
//! \date       Sun Mar 15 20:41:17 2015
//! \brief      AVC engine resource archive.
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

namespace GameRes.Formats.AVC
{
    public class ArchiveFile : ArcFile
    {
        public readonly byte[] Key;

        public ArchiveFile (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AVC"; } }
        public override string Description { get { return "AVC engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        private static readonly string ArchiveKey = "SETSUEI-";

        public override ArcFile TryOpen (ArcView file)
        {
            var header = new byte[0x34];
            if (0x34 != file.View.Read (0, header, 0, 0x34))
                return null;
            var key = new byte[8];
            /*
            for (int i = 0; i < 8; ++i)
                header[8+i] ^= (byte)ArchiveKey[i];
            for (int i = 0; i < 0x24; ++i)
                header[0x10+i] ^= header[8+(i&7)];
            if (!Binary.AsciiEqual (header, 0x10, "ARCHIVE"))
                return null;
            Buffer.BlockCopy (header, 8, key, 0, 8);
            */
            for (int i = 0; i < 8; ++i)
                key[i] = (byte)(header[0x10+i] ^ "ARCHIVE\0"[i]);
            for (int i = 9; i < 0x24; ++i)
                header[0x10+i] ^= key[i&7];
            int index_offset = LittleEndian.ToInt32 (header, 0x20);
            int count = LittleEndian.ToInt32 (header, 0x24);
            if (index_offset < 0x24 || index_offset >= file.MaxOffset || count <= 0 || count > 0xfffff)
                return null;
            var index = new byte[0x114 * count];
            int index_size = file.View.Read (index_offset+0x10, index, 0, (uint)index.Length);
            count = index_size / 0x114;
            if (count * 0x114 != index_size)
                return null;
            for (int i = 0; i < index_size; ++i)
                index[i] ^= key[(index_offset+i)&7];
            var dir = new List<Entry> (count);
            index_offset = 0;
            for (int i = 0; i < count; ++i)
            {
                if (0 != index[index_offset++])
                    return null;
                int name_length = 0;
                while (name_length < 0x100 && 0 != index[index_offset+name_length])
                    name_length++;
                if (0 == name_length)
                {
                    index_offset += 0x113;
                    continue;
                }
                var name = Encodings.cp932.GetString (index, index_offset, name_length);
                var entry = FormatCatalog.Instance.CreateEntry (name);
                index_offset += 0x107;
                entry.Offset = 0x10 + LittleEndian.ToUInt32 (index, index_offset);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+4);
                index_offset += 0x0c;
                dir.Add (entry);
            }
            return new ArchiveFile (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var arcf = arc as ArchiveFile;
            if (null == arcf)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            int base_offset = (int)(entry.Offset-0x10);
            for (int i = 0; i < data.Length; ++i)
                data[i] ^= arcf.Key[((base_offset+i)&7)];
            return new MemoryStream (data, false);
        }
    }
}
