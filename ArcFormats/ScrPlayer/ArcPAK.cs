//! \file       ArcPAK.cs
//! \date       Sat Sep 10 16:00:06 2016
//! \brief      ScrPlayer resource archive.
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

namespace GameRes.Formats.ScrPlayer
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/ScrPlayer"; } }
        public override string Description { get { return "ScrPlayer engine resource archive"; } }
        public override uint     Signature { get { return 0x6B636170; } } // 'pack'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Signatures = new uint[] { 0x6B636170, 0x32636170 }; // 'pack', 'pac2'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            if (index_size < 0x10 || index_size >= file.MaxOffset)
                return null;
            IBinaryStream index;
            bool is_encrypted = file.View.ReadByte (3) == '2';
            if (is_encrypted)
            {
                var index_bytes = new byte[(index_size + 3) & ~3u];
                file.View.Read (8, index_bytes, 0, index_size);
                DecryptIndex (index_bytes, (int)index_size);
                index = new BinMemoryStream (index_bytes, 0, (int)index_size, file.Name);
            }
            else
                index = file.CreateStream (8, index_size);
            using (index)
            {
                uint index_pos = 0;
                var dir = new List<Entry>();
                while (index_pos < index_size)
                {
                    index.Position = index_pos;
                    uint offset = index.ReadUInt32();
                    if (0 == offset)
                        break;
                    uint size   = index.ReadUInt32();
                    byte name_length = index.ReadUInt8 ();
                    var name = index.ReadCString (name_length);
                    index_pos += ((5u + name_length) & ~3u) + 8u;

                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }

        unsafe void DecryptIndex (byte[] data, int length)
        {
            int aligned_count = ((length - 1) >> 2) + 1;
            if (aligned_count * 4 > length)
                throw new ArgumentException ("Can't decrypt non-aligned array.");
            fixed (byte* data8 = data)
            {
                uint* data32 = (uint*)data8;
                for (int i = 0; i < aligned_count; ++i)
                    *data32++ ^= EncryptionKey[i & 7];
            }
        }

        static readonly uint[] EncryptionKey = {
            0x305325A0, 0x306F308C, 0x672C5742, 0x5C0B5343,
            0x8457306E, 0x72694F5C, 0x30423067, 0x0000308B,
        };
    }
}
