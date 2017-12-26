//! \file       ArcARC.cs
//! \date       2017 Dec 25
//! \brief      Succubus resource archive.
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

namespace GameRes.Formats.Succubus
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/ARC1"; } }
        public override string Description { get { return "Succubus resource archive"; } }
        public override uint     Signature { get { return 0x31435241; } } // 'ARC1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset < 0x10)
                return null;
            bool is_voice = Path.GetFileNameWithoutExtension (file.Name).Equals ("voice", StringComparison.InvariantCultureIgnoreCase);

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (index_offset >= file.MaxOffset)
                    return null;
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x10);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x14);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (string.IsNullOrEmpty (entry.Type) && is_voice)
                    entry.Type = "audio";
                dir.Add (entry);
                index_offset += 0x18;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
