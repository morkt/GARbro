//! \file       ArcOVK.cs
//! \date       Mon Apr 18 14:59:12 2016
//! \brief      RealLive engine audio archive.
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

namespace GameRes.Formats.RealLive
{
    [Export(typeof(ArchiveFormat))]
    public class OvkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "OVK"; } }
        public override string Description { get { return "RealLive engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".ovk", StringComparison.InvariantCultureIgnoreCase))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = 4 + (uint)count * 0x10;
            if (data_offset >= file.MaxOffset)
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size   = file.View.ReadUInt32 (index_offset);
                uint offset = file.View.ReadUInt32 (index_offset+4);
                uint id     = file.View.ReadUInt32 (index_offset+8);
                if (offset < data_offset)
                    return null;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D5}.ogg", base_name, id),
                    Type = "audio",
                    Offset = offset,
                    Size   = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
