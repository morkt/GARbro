//! \file       ArcPEL.cs
//! \date       2019 May 28
//! \brief      Dall resource archive.
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

namespace GameRes.Formats.Dall
{
    [Export(typeof(ArchiveFormat))]
    public class PelOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PEL"; } }
        public override string Description { get { return "Dall resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("PEL"))
                return null;
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            using (var input = file.CreateStream())
            {
                input.Position = 2;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = input.ReadCString (0x14);
                    uint size = input.ReadUInt32();
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
