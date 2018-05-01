//! \file       ArcISA.cs
//! \date       Tue Mar 17 09:21:03 2015
//! \brief      ISM engine resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.ISM
{
    [Export(typeof(ArchiveFormat))]
    public class IsaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ISA"; } }
        public override string Description { get { return "ISM engine resource archive"; } }
        public override uint     Signature { get { return 0x204d5349; } } // 'ISM '
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ARCHIVED"))
                return null;
            int count = file.View.ReadInt16 (0x0C);
            if (!IsSaneCount (count))
                return null;
            int version = file.View.ReadUInt16 (0x0E);
            bool is_encrypted = (version & 0x8000) != 0;
            version &= 0x7FFF;
            uint index_offset = 0x10;
            uint name_length   = 1 == version ? 0x30u : 0x0Cu;
            uint record_length = 1 == version ? 0x10u : 0x14u;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                index_offset += name_length;
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += record_length;
            }
            return new ArcFile (file, this, dir);
        }

        unsafe void DecryptIndex (byte[] data)
        {
            int length = data.Length / 4;
            if (0 == length)
                return;
            fixed (byte* data8 = data)
            {
                int* data32 = (int*)data8;
                for (int i = 0; i < length; ++i)
                {
                    data32[i] ^= ~(data.Length + length - i);
                }
            }
        }
    }
}
