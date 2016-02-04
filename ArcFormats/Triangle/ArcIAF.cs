//! \file       ArcIAF.cs
//! \date       Thu Feb 04 03:18:26 2016
//! \brief      route2 engine multi-frame image.
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

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class IafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IAF/MULTI"; } }
        public override string Description { get { return "route2 engine multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public IafOpener ()
        {
            Extensions = new string[] { "iaf" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".iaf", StringComparison.InvariantCultureIgnoreCase)
                || file.MaxOffset < 0x20)
                return null;
            uint size = file.View.ReadUInt32 (1);
            if (size >= file.MaxOffset || size+0x19 >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long current_offset = 0;
            var dir = new List<Entry>();
            while (current_offset < file.MaxOffset)
            {
                uint packed_size = file.View.ReadUInt32 (current_offset+1);
                if (0 == packed_size || current_offset+packed_size+0x19 > file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D3}.IAF", base_name, dir.Count),
                    Type = "image",
                    Offset = current_offset,
                    Size = packed_size + 0x19,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
