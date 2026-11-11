//! \file       ArcKBM.cs
//! \date       2026-04-12
//! \brief      Ruri System image archive format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Ruri {
    [Export(typeof(ArchiveFormat))]
    public class KbmOpener : ArchiveFormat {
        public override string         Tag { get { return "KBM"; } }
        public override string Description { get { return "Ruri System image archive format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public KbmOpener() {
            Extensions = new string[] { "Kbm" };
        }

        public override ArcFile TryOpen(ArcView file) {
            if (!file.View.AsciiEqual(0, "KBM"))
                return null;
            int count = file.View.ReadByte(3);
            var base_name = Path.GetFileNameWithoutExtension(file.Name);
            var dir = new List<Entry>(count);
            int offset = 0x20;
            for (int i = 0; i < count; i++) {
                var entry = new Entry {
                    Name = string.Format("{0}#{1:D3}.bmp", base_name, i),
                    Type = "image",
                    Offset = file.View.ReadUInt32(offset),
                    Size = file.View.ReadUInt32(offset + 4)
                };
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
                offset += 0x10;
            }
            return new ArcFile(file, this, dir);
        }
    }
}
