//! \file       ArcDAT.cs
//! \date       2018 Jun 11
//! \brief      Winters resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [091127][Winters] Kiss x 600 Kanrinin-san no Ponytail

namespace GameRes.Formats.Winters
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CAPYBARA"; } }
        public override string Description { get { return "Winters resource archive"; } }
        public override uint     Signature { get { return 0x59504143; } } // 'CAPYBARA DAT 001'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CAPYBARA DAT 001"))
                return null;
            uint names_offset = file.View.ReadUInt32 (0x10);
            uint names_length = file.View.ReadUInt32 (0x14);
            uint index_offset = 0x18;
            var dir = new List<Entry>();
            using (var names = file.CreateStream (names_offset, names_length))
            using (var index = new StreamReader (names, Encodings.cp932))
            {
                string name;
                while (index_offset < names_offset && (name = index.ReadLine()) != null)
                {
                    if (":END" == name)
                        break;
                    if (name.Length > 0)
                    {
                        var entry = FormatCatalog.Instance.Create<Entry> (name);
                        entry.Offset = file.View.ReadUInt32 (index_offset);
                        entry.Size   = file.View.ReadUInt32 (index_offset+4);
                        if (!entry.CheckPlacement (file.MaxOffset))
                            return null;
                        dir.Add (entry);
                    }
                    index_offset += 8;
                }
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
