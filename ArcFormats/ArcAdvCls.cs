//! \file       ArcAdvCls.cs
//! \date       Sun Jul 05 04:42:34 2015
//! \brief      AdvCls engine resource archive.
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
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AdvCls
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/AdvCls"; } }
        public override string Description { get { return "AdvCls engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        static readonly string KnownKey = "kimiomo"; // "Kimi no Omoi, Sono Negai"

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfffff)
                return null;
            int compressed = file.View.ReadInt32 (4);
            if (0 != compressed && 1 != compressed)
                return null;
            var index = new byte[count*0x24];
            if (index.Length != file.View.Read (8, index, 0, (uint)index.Length))
                return null;
            DecryptIndex (index, KnownKey);
            int index_offset = 0;
            int data_offset = index.Length + 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = Binary.GetCString (index, index_offset, 0x18);
                var entry = new PackedEntry {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    IsPacked = compressed != 0,
                };
                index_offset += 0x18;
                entry.UnpackedSize = LittleEndian.ToUInt32 (index, index_offset);
                entry.Size = LittleEndian.ToUInt32 (index, index_offset+4);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+8);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var packed = entry as PackedEntry;
            if (null == packed || !packed.IsPacked)
                return input;
            return new LzssStream (input);
        }

        private void DecryptIndex (byte[] data, string key)
        {
            int k = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] += (byte)key[k++];
                if (key.Length == k)
                    k = 0;
            }
        }
    }
}
