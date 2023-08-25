//! \file       ArcCFP.cs
//! \date       2022 May 17
//! \brief      Winters resource archive.
//
// Copyright (C) 2022 by morkt
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

// [110527][Winters] Kiss x 700 Kiss Tantei

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class CfpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CFP/CAPYBARA"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x59504143; } } // 'CAPYBARA DAT 002'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CAPYBARA DAT 002"))
                return null;
            uint names_offset = file.View.ReadUInt32 (0x14);
            uint names_length = file.View.ReadUInt32 (0x18);
            uint index_offset = 0x20;
            var dir = new List<Entry>();
            using (var names = file.CreateStream (names_offset, names_length))
            using (var index = new StreamReader (names, Encodings.cp932))
            {
                string name;
                while (index_offset < names_offset && (name = index.ReadLine()) != null)
                {
                    if (name.Length > 0)
                    {
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = file.View.ReadUInt32 (index_offset);
                        entry.Size   = file.View.ReadUInt32 (index_offset+4);
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    index_offset += 0xC;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
