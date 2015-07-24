//! \file       ArcSUD.cs
//! \date       Sat Jul 25 01:53:04 2015
//! \brief      Triangle audio archive.
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

namespace GameRes.Formats.Triangle
{
    [Export(typeof(ArchiveFormat))]
    public class SudOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SUD"; } }
        public override string Description { get { return "Triangle audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "OggS"))
                return null;

            uint current_offset = 0;
            uint first_size = file.View.ReadUInt32 (current_offset);
            if (first_size >= file.MaxOffset)
                return null;

            var dir = new List<Entry>();
            int n = 0;
            while (current_offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (current_offset);
                if (current_offset + 4 + (long)size > file.MaxOffset)
                    return null;
                if (file.View.AsciiEqual (current_offset+4, "OggS"))
                {
                    var entry = new Entry {
                        Name    = string.Format ("{0:D5}.ogg", n++),
                        Type    = "audio",
                        Offset  = current_offset + 4,
                        Size    = size,
                    };
                    dir.Add (entry);
                }
                current_offset += 4+size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
