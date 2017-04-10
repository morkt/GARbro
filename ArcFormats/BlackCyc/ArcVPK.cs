//! \file       ArcVPK.cs
//! \date       Sat Aug 01 11:37:55 2015
//! \brief      Black Cyc engine audio archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.BlackCyc
{
    [Export(typeof(ArchiveFormat))]
    public class VpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VPK"; } }
        public override string Description { get { return "Black Cyc engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".vpk"))
                return null;
            var vtb_name = Path.ChangeExtension (file.Name, "vtb");
            if (!VFS.FileExists (vtb_name))
                return null;
            var vtb_entry = VFS.FindFile (vtb_name);
            int count = (int)(vtb_entry.Size / 0x0C) - 1;
            if (!IsSaneCount (count))
                return null;

            using (var vtb = VFS.OpenView (vtb_entry))
            {
                vtb.View.Reserve (0, (uint)vtb.MaxOffset);
                uint index_offset = 0;
                uint next_offset = vtb.View.ReadUInt32 (8);
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    string name = vtb.View.ReadString (index_offset, 8) + ".vaw";
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = next_offset;
                    index_offset += 0xC;
                    next_offset = vtb.View.ReadUInt32 (index_offset+8);
                    entry.Size = next_offset - (uint)entry.Offset;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
