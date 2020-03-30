//! \file       ArcDAT.cs
//! \date       2019 Jun 06
//! \brief      AOS engine resource archive.
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

// [001124][emu] Luna Season ~150 Bun no 1 no Koibito~

namespace GameRes.Formats.Aos
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PACK"; } }
        public override string Description { get { return "AOS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var idx_name = VFS.ChangeFileName (file.Name, "index.idx");
            if (!VFS.FileExists (idx_name))
                return null;
            using (var index = VFS.OpenBinaryStream (idx_name))
            {
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    var name = index.ReadCString (0x34);
                    if (string.IsNullOrEmpty (name))
                        break;
                    var entry = Create<Entry> (name);
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
