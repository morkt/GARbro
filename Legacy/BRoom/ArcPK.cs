//! \file       ArcPK.cs
//! \date       2019 Jan 04
//! \brief      Studio B-Room resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.BRoom
{
    [Export(typeof(ArchiveFormat))]
    public class PkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PK/B-ROOM"; } }
        public override string Description { get { return "Studio B-Room resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PkOpener ()
        {
            Extensions = new[] { "pk", "cpc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 4;
            long data_offset = count * 0x18 + index_offset;
            if (data_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset+4);
                var name = file.View.ReadString (index_offset+8, 0x10);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                data_offset += size;
                index_offset += 0x18;
            }
            if (data_offset != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class EncryptedPkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PK/B-ROOM/E"; } }
        public override string Description { get { return "Studio B-Room encrypted resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly byte[] DefaultNameKey = {
            0xF5, 0xB2, 0xA4, 0x45, 0x59, 0x0F, 0x15, 0x22, 0x43, 0x0B, 0x99, 0x3C, 0xDD, 0xE2
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = (int)(file.View.ReadUInt32 (0) ^ 0xFF559977);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 4;
            long data_offset = count * 0x18 + index_offset;
            if (data_offset >= file.MaxOffset)
                return null;
            var name_key = DefaultNameKey;
            var name_buffer = new byte[0x10];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size = file.View.ReadUInt32 (index_offset+4);
                file.View.Read (index_offset+8, name_buffer, 0, 14);
                uint checksum = 0;
                int j;
                for (j = 0; j < 14; ++j)
                {
                    name_buffer[j] ^= name_key[j];
                    if (0 == name_buffer[j])
                        break;
                    checksum += (uint)name_buffer[j] << ((j & 3) << 3);
                }
                checksum &= 0x3FF;
                var name = Encodings.cp932.GetString (name_buffer, 0, j);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                if (name.HasAnyOfExtensions ("", "e", "er"))
                    name = Path.ChangeExtension (name, ".Erp");
                var entry = Create<Entry> (name);
                entry.Offset = offset ^ checksum ^ 0x35846;
                entry.Size   = size   ^ checksum ^ 0x57982525;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
