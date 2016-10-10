//! \file       ArcSNN.cs
//! \date       Sun Jan 10 01:51:27 2016
//! \brief      BlueGale resource archive.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.BlueGale
{
    [Export(typeof(ArchiveFormat))]
    public class SnnOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SNN"; } }
        public override string Description { get { return "BlueGale resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".snn", StringComparison.InvariantCultureIgnoreCase))
                return null;
            var inx_name = Path.ChangeExtension (file.Name, "Inx");
            if (!VFS.FileExists (inx_name))
                return null;
            using (var inx = VFS.OpenView (inx_name))
            {
                int count = inx.View.ReadInt32 (0);
                if (!IsSaneCount (count))
                    return null;

                int inx_offset = 4;
                if (inx_offset + count * 0x48 > inx.MaxOffset)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = inx.View.ReadString (inx_offset, 0x40);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = inx.View.ReadUInt32 (inx_offset+0x40);
                    entry.Size   = inx.View.ReadUInt32 (inx_offset+0x44);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    inx_offset += 0x48;
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
