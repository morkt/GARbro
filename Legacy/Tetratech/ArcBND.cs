//! \file       ArcBND.cs
//! \date       2018 Jul 03
//! \brief      Tetratech resource archive.
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
using System.IO;

// [010309][Tetratech] Kyouiku Jisshuu 2 ~Joshikousei Maniacs~

namespace GameRes.Formats.Tetratech
{
    [Export(typeof(ArchiveFormat))]
    public class BndOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BND/IDX"; } }
        public override string Description { get { return "Tetratech resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".BND"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, "idx");
            if (!VFS.FileExists (idx_name))
                return null;
            using (var idx = VFS.OpenBinaryStream (idx_name))
            {
                int count = (int)idx.Length / 0x18;
                if (!IsSaneCount (count) || count * 0x18 != idx.Length)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = idx.ReadCString (0x10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size   = idx.ReadUInt32();
                    entry.Offset = idx.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
