//! \file       ArcLPK.cs
//! \date       2022 Apr 12
//! \brief      Kogado resource archive.
//
// Copyright (C) 2022 by morkt
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

namespace GameRes.Formats.Kogado
{
    [Export(typeof(ArchiveFormat))]
    public class LpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LPK/KOGADO"; } }
        public override string Description { get { return "Kogado resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".lpk") || file.MaxOffset < 0x2800)
                return null;
            uint index_offset = 0;
            var dir = new List<Entry> ();
            for (int i = 0; i < 0x200; ++i)
            {
                if (file.View.ReadByte (index_offset) == 0)
                    break;
                var name = file.View.ReadString (index_offset, 0x10);
                if (!IsValidEntryName (name))
                    return null;
                index_offset += 0x10;
                var entry = Create<Entry> (name);
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            index_offset = 0x2000;
            uint base_offset = 0x2800;
            uint offset = file.View.ReadUInt32 (index_offset);
            foreach (var entry in dir)
            {
                index_offset += 4;
                uint next_offset = file.View.ReadUInt32 (index_offset);
                uint size = next_offset - offset;
                entry.Offset = offset + base_offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset = next_offset;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
