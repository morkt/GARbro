//! \file       ArcBSC.cs
//! \date       Sat Oct 24 18:26:41 2015
//! \brief      Bishop composite image archive.
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

namespace GameRes.Formats.Bishop
{
    [Export(typeof(ArchiveFormat))]
    public class BscOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BSC"; } }
        public override string Description { get { return "Bishop composite image archive"; } }
        public override uint     Signature { get { return 0x2D535342; } } // 'BSS-'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public BscOpener ()
        {
            Extensions = new string[] { "bsc", "bsg" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "BSS-Composition\0"))
                return null;
            int count = file.View.ReadByte (0x11);
            if (0 == count)
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            uint current_offset = 0x20;
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name    = string.Format ("{0}#{1:D3}.bsg", base_name, i),
                    Type    = "image",
                    Offset  = current_offset,
                    Size    = 0x40 + file.View.ReadUInt32 (current_offset+0x36),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
