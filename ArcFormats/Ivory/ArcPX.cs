//! \file       ArcPX.cs
//! \date       Mon Sep 12 13:12:04 2016
//! \brief      'fPX' audio archive.
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

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ArchiveFormat))]
    public class PxOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PX/IVORY"; } }
        public override string Description { get { return "Ivory audio archive"; } }
        public override uint     Signature { get { return 0x20585066; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset != file.View.ReadUInt32 (4))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = 8;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                if (!file.View.AsciiEqual (offset, "cTRK"))
                    break;
                uint size = file.View.ReadUInt32 (offset+4);
                if (0 == size)
                    return null;
                int num = file.View.ReadInt32 (offset+8);
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}.trk", base_name, num),
                    Type = "audio",
                    Offset = offset,
                    Size = size
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                offset += size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
