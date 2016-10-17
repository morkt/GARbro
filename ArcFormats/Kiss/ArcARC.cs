//! \file       ArcARC.cs
//! \date       Fri Jul 22 06:53:47 2016
//! \brief      Kiss resource archive.
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

namespace GameRes.Formats.Kiss
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/KISS"; } }
        public override string Description { get { return "Kiss resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".arc", StringComparison.InvariantCultureIgnoreCase))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            using (var input = file.CreateStream())
            {
                long prev_offset = 4;
                input.Position = 4;
                for (int i = 0; i < count; ++i)
                {
                    var name = input.ReadCString();
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = input.ReadInt64();
                    if (entry.Offset < prev_offset || entry.Offset > file.MaxOffset)
                        return null;
                    dir.Add (entry);
                    prev_offset = entry.Offset;
                }
                for (int i = 0; i < count; ++i)
                {
                    long next = i+1 < count ? dir[i+1].Offset : file.MaxOffset;
                    dir[i].Size = (uint)(next - dir[i].Offset);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
