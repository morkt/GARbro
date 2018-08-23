//! \file       ArcDAT.cs
//! \date       2018 Aug 22
//! \brief      KeroQ resource archive.
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

namespace GameRes.Formats.KeroQ
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PAC"; } }
        public override string Description { get { return "KeroQ resource archive"; } }
        public override uint     Signature { get { return 0x43415089; } } // '\x89PAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var pac_name = Path.GetFileNameWithoutExtension (file.Name);
            int pac_num;
            if (!Int32.TryParse (pac_name, out pac_num))
                return null;
            var hdr_name = string.Format ("{0:D3}.dat", pac_num - 1);
            hdr_name = VFS.ChangeFileName (file.Name, hdr_name);
            if (!VFS.FileExists (hdr_name))
                return null;
            using (var index = VFS.OpenBinaryStream (hdr_name))
            {
                var header = index.ReadHeader (8);
                if (!header.AsciiEqual ("\x89HDR"))
                    return null;
                if (header.ToInt32 (4) != count)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
