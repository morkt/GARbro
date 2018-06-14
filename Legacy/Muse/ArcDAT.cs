//! \file       ArcDAT.cs
//! \date       2018 Jun 06
//! \brief      Muse engine resource archive.
//
// Copyright (C) 2018 by morkt
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

// [020125][Luchs] Sollunea

namespace GameRes.Formats.Muse
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MUSE"; } }
        public override string Description { get { return "Muse engine resource archive"; } }
        public override uint     Signature { get { return 0x4553554D; } } // 'MUSE'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x16;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size   = file.View.ReadUInt32 (index_offset+2);
                uint offset = file.View.ReadUInt32 (index_offset+7);
                var name = file.View.ReadString (index_offset+0xB, 0x100);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10B;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
