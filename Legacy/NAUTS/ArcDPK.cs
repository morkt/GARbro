//! \file       ArcDPK.cs
//! \date       2026-10-01
//! \brief      NAUTS resource archive format.
//
// Copyright (C) 2026 by morkt
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.Nauts
{
    [Export(typeof(ArchiveFormat))]
    public class DpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DPK/NAUTS"; } }
        public override string Description { get { return "NAUTS DPK archive"; } }
        public override uint     Signature { get { return 0x44495246; } } // 'FRID'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DpkOpener ()
        {
            Extensions = new string[] { "dpk" };
            Signatures = new uint[] { 0x44495246, 0x46524944 }; // 'FRID' / 'DIRF'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint magic = file.View.ReadUInt32 (0);
            bool little_endian = 0x44495246 == magic; // 'FRID'
            if (!little_endian && 0x46524944 != magic) // 'DIRF'
                return null;
            uint block_size  = ReadU32 (file, 8,    little_endian);
            int  count       = (int)ReadU32 (file, 0xC,  little_endian);
            uint header_size = ReadU32 (file, 0x10, little_endian);
            uint entry_size  = ReadU32 (file, 0x14, little_endian);
            if (!IsSaneCount (count) || 0x800 != block_size || 0x18 != entry_size)
                return null;
            long index_size = HeaderSize + (long)count * entry_size;
            if (header_size < index_size || header_size > file.MaxOffset || 0 != (header_size % block_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = HeaderSize;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 12);
                if (0 == name.Length)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = ReadU32 (file, index_offset + 12, little_endian);
                entry.Size   = ReadU32 (file, index_offset + 16, little_endian);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        static uint ReadU32 (ArcView file, long offset, bool little_endian)
        {
            uint value = file.View.ReadUInt32 (offset);
            return little_endian ? value : Binary.BigEndian (value);
        }

        const int HeaderSize = 0x20;
    }
}