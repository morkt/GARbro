//! \file       ArcSDA.cs
//! \date       2018 Oct 08
//! \brief      MMFass engine resource archive.
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

namespace GameRes.Formats.Mmfass
{
    [Export(typeof(ArchiveFormat))]
    public class SdaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SDA/MMFASS"; } }
        public override string Description { get { return "MMFass engine resource archive"; } }
        public override uint     Signature { get { return 0x4153; } } // 'SA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public SdaOpener ()
        {
            Signatures = new uint[] { 0x30004153, 0x4153, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "SA"))
                return null;
            uint data_offset = file.View.ReadUInt32 (4);
            if (data_offset <= 8 || data_offset >= file.MaxOffset)
                return null;
            int count = (int)(data_offset - 8) / 0x1C;
            if (!IsSaneCount (count))
                return null;
            bool is_graphic = Path.GetFileNameWithoutExtension (file.Name).Equals ("g", StringComparison.OrdinalIgnoreCase);
            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x14).Trim();
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14) + data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (is_graphic)
                    entry.Type = "image";
                dir.Add (entry);
                index_offset += 0x1C;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
