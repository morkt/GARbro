//! \file       ArcAFA.cs
//! \date       Mon Apr 25 18:18:57 2016
//! \brief      AliceSoft System 4 engine resource archive.
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

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AfaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AFA"; } }
        public override string Description { get { return "AliceSoft System 4 resource archive"; } }
        public override uint     Signature { get { return 0x48414641; } } // 'AFAH'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (8, "AlicArch"))
                return null;
            if (!file.View.AsciiEqual (0x1C, "INFO"))
                return null;
            int version = file.View.ReadInt32 (0x10);
            long base_offset = file.View.ReadUInt32 (0x18);
            uint packed_size = file.View.ReadUInt32 (0x20);
            int unpacked_size = file.View.ReadInt32 (0x24);
            int count = file.View.ReadInt32 (0x28);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            var name_buf = new byte[0x40];
            using (var input = file.CreateStream (0x2C, packed_size))
            using (var zstream = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryReader (zstream))
            {
                for (int i = 0; i < count; ++i)
                {
                    int name_length = index.ReadInt32();
                    int index_step = index.ReadInt32();
                    if (name_length <= 0 || name_length > index_step || index_step > unpacked_size)
                        return null;
                    if (index_step > name_buf.Length)
                        name_buf = new byte[index_step];
                    if (index_step != index.Read (name_buf, 0, index_step))
                        return null;
                    var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    index.ReadInt32();
                    index.ReadInt32();
                    if (version < 2)
                        index.ReadInt32();
                    entry.Offset = index.ReadUInt32() + base_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        static readonly byte[] AffKey = {
            0xC8, 0xBB, 0x8F, 0xB7, 0xED, 0x43, 0x99, 0x4A,
            0xA2, 0x7E, 0x5B, 0xB0, 0x68, 0x18, 0xF8, 0x88
        };

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 0x10 || !arc.File.View.AsciiEqual (entry.Offset, "AFF\0"))
                return base.OpenEntry (arc, entry);
            uint data_size = entry.Size - 0x10u;
            uint encrypted_length = Math.Min (0x40u, data_size);
            var prefix = arc.File.View.ReadBytes (entry.Offset+0x10, encrypted_length);
            for (int i = 0; i < prefix.Length; ++i)
                prefix[i] ^= AffKey[i & 0xF];
            if (data_size <= 0x40)
                return new MemoryStream (prefix);
            var rest = arc.File.CreateStream (entry.Offset+0x10+encrypted_length, data_size-encrypted_length);
            return new PrefixStream (prefix, rest);
        }
    }
}
