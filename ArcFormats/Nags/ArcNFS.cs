//! \file       ArcNFS.cs
//! \date       Sun Nov 08 15:56:33 2015
//! \brief      NA Game System resource archive.
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
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Nags
{
    [Export(typeof(ArchiveFormat))]
    public class NfsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "NFS"; } }
        public override string Description { get { return "NAGS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index_size = (uint)count * 0x20;
            if (index_size > file.View.Reserve (4, (uint)index_size))
                return null;
            int first_offset = file.View.ReadInt32 (0x1C);
            byte key = (byte)count;
            first_offset ^= key | key << 8 | key << 16 | key << 24;
            if (first_offset != 0)
                return null;

            var index = file.View.ReadBytes (4, index_size);
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= key;

            long base_offset = 4 + index_size;
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset, 0x18);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + LittleEndian.ToUInt32 (index, index_offset+0x18);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!entry.Name.EndsWith (".scb", StringComparison.InvariantCultureIgnoreCase))
                return input;
            return new InputCryptoStream (input, new NotTransform());
        }
    }
}
