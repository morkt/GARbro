//! \file       ArcVAFS.cs
//! \date       Sun Jan 10 04:19:47 2016
//! \brief      Softpal engine resource archive.
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

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ArchiveFormat))]
    public class VafsOpener : ArchiveFormat
    {
        public override string         Tag { get { return "VAFS"; } }
        public override string Description { get { return "Softpal engine resource archive"; } }
        public override uint     Signature { get { return 0x53464156; } } // 'VAFS'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public VafsOpener ()
        {
            Extensions = new string[] { "052" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if ('H' != file.View.ReadByte (4))
                return null;
            uint index_offset = 0x10;
            uint data_offset = file.View.ReadUInt32 (index_offset);
            if (data_offset < index_offset || data_offset >= file.MaxOffset)
                return null;
            int count = (int)(data_offset - index_offset) / 4;
            if (!IsSaneCount (count))
                return null;

            uint next_offset = data_offset;
            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
            bool is_audio = "BGM" == base_name;
            var dir = new List<Entry> (count);
            for (int i = 0; next_offset != 0 && next_offset != file.MaxOffset && i < count; ++i)
            {
                index_offset += 4;
                var name = string.Format("{0}#{1:D5}", base_name, i);
                var entry = AutoEntry.Create (file, next_offset, name);
                next_offset = index_offset == data_offset ? 0 : file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)((0 != next_offset ? (long)next_offset : file.MaxOffset) - entry.Offset);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
