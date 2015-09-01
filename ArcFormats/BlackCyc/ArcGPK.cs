//! \file       ArcGPK.cs
//! \date       Sat Aug 01 12:01:26 2015
//! \brief      Black Cyc engine images archive.
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
    public class GpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GPK"; } }
        public override string Description { get { return "Black Cyc engine images archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".gpk", StringComparison.InvariantCultureIgnoreCase))
                return null;
            var gtb_name = Path.ChangeExtension (file.Name, "gtb");
            if (!VFS.FileExists (gtb_name))
                return null;
            using (var gtb = VFS.OpenView (gtb_name))
            {
                int count = gtb.View.ReadInt32 (0);
                if (!IsSaneCount (count))
                    return null;

                gtb.View.Reserve (0, (uint)gtb.MaxOffset);
                int name_index = 4;
                int offsets_index = name_index + count * 4;
                int name_base = offsets_index + count * 4;
                if (name_base >= gtb.MaxOffset)
                    return null;
                uint next_offset = gtb.View.ReadUInt32 (offsets_index);
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    offsets_index += 4;
                    int name_offset = name_base + gtb.View.ReadInt32 (name_index);
                    name_index += 4;
                    if (name_offset < name_base || name_offset >= gtb.MaxOffset)
                        return null;
                    string name = gtb.View.ReadString (name_offset, (uint)(gtb.MaxOffset-name_offset));
                    name += ".dwq";
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = next_offset;
                    if (i + 1 == count)
                        next_offset = (uint)file.MaxOffset;
                    else
                        next_offset = gtb.View.ReadUInt32 (offsets_index);
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
