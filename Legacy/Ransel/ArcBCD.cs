//! \file       ArcBCD.cs
//! \date       2019 Jun 02
//! \brief      ransel engine resource archive.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Ransel
{
    [Export(typeof(ArchiveFormat))]
    public class BcdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BCD"; } }
        public override string Description { get { return "ransel engine resource archive"; } }
        public override uint     Signature { get { return 0x616E6942; } } // 'BinaryCombineData'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "BinaryCombineData"))
                return null;
            var bcl_name = Path.ChangeExtension (file.Name, "bcl");
            using (var bcl = VFS.OpenStream (bcl_name))
            using (var index = new StreamReader (bcl, Encodings.cp932))
            {
                if (index.ReadLine() != "[BinaryCombineData]")
                    return null;
                var filename = index.ReadLine();
                if (!VFS.IsPathEqualsToFileName (file.Name, filename))
                    return null;
                index.ReadLine();
                var dir = new List<Entry>();
                while ((filename = index.ReadLine()) != null)
                {
                    if (!filename.StartsWith ("[") || !filename.EndsWith ("]"))
                        return null;
                    filename = filename.Substring (1, filename.Length-2);
                    var offset = index.ReadLine();
                    var size = index.ReadLine();
                    index.ReadLine();
                    var entry = Create<Entry> (filename);
                    entry.Offset = UInt32.Parse (offset);
                    entry.Size   = UInt32.Parse (size);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
