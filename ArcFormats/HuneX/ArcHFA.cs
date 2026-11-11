//! \file       ArcHFA.cs
//! \date       2026-01-18
//! \brief      HUNEX General Game Engine resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.HuneX {
    [Export(typeof(ArchiveFormat))]
    public class HfaOpener : BGI.Arc2Opener {
        public override string         Tag { get { return "HFA"; } }
        public override string Description { get { return "HuneX general game engine resource archive"; } }
        public override uint     Signature { get { return 0x454e5548; } } // "HUNE"
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public HfaOpener() {
            Extensions = new string[] { "hfa" };
            ContainedFormats = new[] { "BGI", "CompressedBG_MT", "BW", "SCR" };
        }

        public override ArcFile TryOpen(ArcView file) {
            if (!file.View.AsciiEqual(4, "XGGEFA10"))
                return null;
            return Open(file);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            if (!arc.File.View.AsciiEqual(entry.Offset, "LenZuCompressor"))
                return base.OpenEntry(arc, entry);
            var decoder = new LenZuDecoder(arc.File.View.ReadBytes(entry.Offset + 0x20, entry.Size - 0x20));
            return new BinMemoryStream(decoder.Unpack());
        }
    }
}
