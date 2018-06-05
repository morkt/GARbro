//! \file       ArcFPK2.cs
//! \date       2018 Jun 02
//! \brief      FPK 2.0 resource archives implementation.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.CandySoft
{
    [Export(typeof(ArchiveFormat))]
    public class Fpk2Opener : FpkOpener
    {
        public override string         Tag { get { return "FPK/2.00"; } }
        public override string Description { get { return "Interheart resource archive"; } }
        public override uint     Signature { get { return 0x204B5046; } } // 'FPK 2.00'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "2.00"))
                return null;
            int count = file.View.ReadInt32 (0x1C);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x20;
            uint index_size = (uint)count * 0x20u;
            if (index_size > file.View.Reserve (index_offset, index_size))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset+8, 0x18);
                if (!name.StartsWith ("/"))
                {
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset);
                    entry.Size   = file.View.ReadUInt32 (index_offset+4);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
