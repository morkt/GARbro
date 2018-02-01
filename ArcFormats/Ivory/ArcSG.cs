//! \file       ArcSG.cs
//! \date       2018 Feb 01
//! \brief      'fSGX' multi-frame image container.
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

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ArchiveFormat))]
    public class SgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SG/cOBJ"; } }
        public override string Description { get { return "Ivory multi-frame image"; } }
        public override uint     Signature { get { return 0x58475366; } } // 'fSGX'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long offset = 8;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            while (offset < file.MaxOffset && file.View.AsciiEqual (offset, "cOBJ"))
            {
                uint obj_size = file.View.ReadUInt32 (offset+4);
                if (0 == obj_size)
                    break;
                if (file.View.AsciiEqual (offset+0x10, "fSG "))
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}", base_name, dir.Count),
                        Type = "image",
                        Offset = offset+0x10,
                        Size = file.View.ReadUInt32 (offset+0x14),
                    };
                    dir.Add (entry);
                }
                offset += obj_size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
