//! \file       ArcAPG.cs
//! \date       2026-09-08
//! \brief      aiwin resource archive.
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

namespace GameRes.Formats.Scoop
{
    [Export(typeof(ArchiveFormat))]
    public class ApgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "APG"; } }
        public override string Description { get { return "Scoop resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ApgOpener ()
        {
            Extensions = new string[] { "apg", "wax", "six" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            string extension;
            if (file.Name.HasExtension (".apg"))
                extension = "apx";
            else if (file.Name.HasExtension (".wax"))
                extension = "wav";
            else if (file.Name.HasExtension (".six"))
                extension = "mds";
            else
                return null;

            int count = file.View.ReadByte (0);
            if (count == 0)
                return null;
            uint offset = file.View.ReadUInt32 (1);
            if ((int)(count * 4 + 5) != offset)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint next_offset = file.View.ReadUInt32 (i * 4 + 5);
                var entry = Create<Entry> (string.Format ("{0}#{1:D3}.{2}", base_name, i, extension));
                entry.Offset = offset;
                entry.Size = next_offset - offset;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset = next_offset;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
