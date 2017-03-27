//! \file       ArcMJA.cs
//! \date       Mon Mar 27 07:22:26 2017
//! \brief      Artemis engine animation resource.
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

namespace GameRes.Formats.Artemis
{
    [Export(typeof(ArchiveFormat))]
    public class MjaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MJA"; } }
        public override string Description { get { return "Artemis engine animation"; } }
        public override uint     Signature { get { return 0x30414A4D; } } // 'MJA0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = 8;
            var dir = new List<Entry>();
            int i = 0;
            while (offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (offset);
                offset += 4;
                var entry = new Entry {
                    Name    = string.Format ("{0}#{1:D4}", base_name, i++),
                    Offset  = offset,
                    Size    = size
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                uint signature = file.View.ReadUInt32 (offset);
                var res = AutoEntry.DetectFileType (signature);
                entry.ChangeType (res);
                offset += size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
