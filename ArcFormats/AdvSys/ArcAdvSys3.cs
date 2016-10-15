//! \file       ArcAdvSys3.cs
//! \date       Thu Oct 13 03:13:16 2016
//! \brief      AdvSys3 resource archive.
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
using System.Linq;

namespace GameRes.Formats.AdvSys
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ADVSYS3"; } }
        public override string Description { get { return "AdvSys3 engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".dat", StringComparison.InvariantCultureIgnoreCase)
                || !Path.GetFileName (file.Name).StartsWith ("arc", StringComparison.InvariantCultureIgnoreCase))
                return null;
            long current_offset = 0;
            var dir = new List<Entry>();
            while (current_offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (current_offset);
                if (0 == size)
                    break;
                uint name_length = file.View.ReadUInt16 (current_offset+8);
                if (0 == name_length || name_length > 0x100)
                    return null;
                var name = file.View.ReadString (current_offset+10, name_length);
                if (0 == name.Length)
                    return null;
                current_offset += 10 + name_length;
                if (current_offset + size > file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = name,
                    Offset = current_offset,
                    Size = size,
                };
                uint signature = file.View.ReadUInt32 (current_offset);
                if (file.View.AsciiEqual (current_offset+4, "GWD"))
                {
                    entry.Type = "image";
                    entry.Name = Path.ChangeExtension (entry.Name, "gwd");
                }
                else
                {
                    var res = AutoEntry.DetectFileType (signature);
                    if (res != null)
                    {
                        entry.Type = res.Type;
                        entry.Name = Path.ChangeExtension (entry.Name, res.Extensions.FirstOrDefault());
                    }
                }
                dir.Add (entry);
                current_offset += size;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
