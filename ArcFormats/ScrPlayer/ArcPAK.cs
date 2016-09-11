//! \file       ArcPAK.cs
//! \date       Sat Sep 10 16:00:06 2016
//! \brief      ScrPlayer resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.ScrPlayer
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/ScrPlayer"; } }
        public override string Description { get { return "ScrPlayer engine resource archive"; } }
        public override uint     Signature { get { return 0x6B636170; } } // 'pack'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_size = file.View.ReadUInt32 (4);
            if (index_size < 0x10 || index_size >= file.MaxOffset)
                return null;

            uint index_offset = 8;
            uint index_end = index_offset + index_size;
            var dir = new List<Entry>();
            while (index_offset < index_end)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                if (0 == offset)
                    break;
                uint size   = file.View.ReadUInt32 (index_offset+4);
                byte name_length = file.View.ReadByte (index_offset+8);
                var name = file.View.ReadString (index_offset+9, name_length);
                index_offset += ((9u + name_length) & ~7u) + 8u;
                if (index_offset > index_end)
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size   = size;
                dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }
    }
}
