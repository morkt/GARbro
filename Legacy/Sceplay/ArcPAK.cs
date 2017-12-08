//! \file       ArcPAK.cs
//! \date       2017 Dec 07
//! \brief      Sceplay engine resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Linq;

namespace GameRes.Formats.Sceplay
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/SCEPLAY"; } }
        public override string Description { get { return "Sceplay engine resource archive"; } }
        public override uint     Signature { get { return 0x006B6170; } } // 'pak'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            using (var index = file.CreateStream())
            {
                index.Position = 8;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x18);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    dir.Add (entry);
                }
                for (int i = 0; i < count; ++i)
                    dir[i].Size = index.ReadUInt32();
                for (int i = 0; i < count; ++i)
                    dir[i].Offset = index.ReadUInt32();
                dir = dir.Where (e => e.Offset != uint.MaxValue).ToList();
                if (dir.Any (e => !e.CheckPlacement (file.MaxOffset)))
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
