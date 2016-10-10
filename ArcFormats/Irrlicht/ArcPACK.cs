//! \file       ArcPACK.cs
//! \date       Sun Aug 28 06:42:38 2016
//! \brief      Irrlicht engine audio archive.
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

namespace GameRes.Formats.Irrlicht
{
    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/Irrlicht"; } }
        public override string Description { get { return "Irrlicht engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "pack" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".pack", StringComparison.InvariantCultureIgnoreCase))
                return null;
            long offset = 0;
            var dir = new List<Entry>();
            while (offset < file.MaxOffset)
            {
                if (offset + 0x10A >= file.MaxOffset)
                    return null;
                var name = file.View.ReadString (offset, 0x104);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                offset += 0x105;
                uint size = file.View.ReadUInt32 (offset);
                offset += 5;
                if (offset + size > file.MaxOffset)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                dir.Add (entry);
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
