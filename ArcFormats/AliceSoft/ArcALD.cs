//! \file       ArcALD.cs
//! \date       Thu Apr 09 20:33:19 2015
//! \brief      AliceSoft System engine resource archive.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AldOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ALD"; } }
        public override string Description { get { return "AliceSoft System engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            long index_offset = file.MaxOffset - 0x10;
            if (index_offset <= 0)
                return null;
            if (0x014c4e != file.View.ReadUInt32 (index_offset)
                || 0x10 != file.View.ReadUInt32 (index_offset+4))
                return null;
            int count = file.View.ReadUInt16 (index_offset+9);
            if (0 == count)
                return null;
            uint index_length = (file.View.ReadUInt32 (0) & 0xffffff) << 8;
            if (index_length > file.View.Reserve (0, index_length))
                return null;
            var dir = new List<Entry> (count);
            index_offset = 3;
            for (int i = 0; i < count; ++i)
            {
                uint offset = (file.View.ReadUInt32 (index_offset) & 0xffffff) << 8;
                if (0 == offset)
                    break;
                if (offset >= file.MaxOffset)
                    return null;
                dir.Add (new Entry { Offset = offset });
                index_offset += 3;
            }
            foreach (var entry in dir)
            {
                var offset = entry.Offset;
                uint header_size = file.View.ReadUInt32 (offset);
                if (header_size <= 0x10)
                    return null;
                entry.Size = file.View.ReadUInt32 (offset+4);
                entry.Name = file.View.ReadString (offset+0x10, header_size-0x10);
                entry.Offset = offset + header_size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
