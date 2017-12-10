//! \file       ArcDNS.cs
//! \date       2017 Dec 10
//! \brief      DarkNiteSystem resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.DarkNiteSystem
{
    [Export(typeof(ArchiveFormat))]
    public class DnsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DNS"; } }
        public override string Description { get { return "DarkNiteSystem resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith ("data.dns", StringComparison.InvariantCultureIgnoreCase))
                return null;
            if (file.View.ReadUInt32 (8) != 0x8000) // first file offset
                return null;

            var dir = new List<Entry>();
            uint index_offset = 0;
            while (index_offset < 0x8000)
            {
                var name = file.View.ReadString (index_offset, 8);
                if (0 == name.Length)
                    break;
                var entry = new Entry {
                    Name   = Path.ChangeExtension (name, "S"),
                    Type   = "script",
                    Offset = file.View.ReadUInt32 (index_offset+8),
                    Size   = file.View.ReadUInt32 (index_offset+12),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = (byte)-data[i];
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
