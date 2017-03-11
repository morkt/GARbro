//! \file       ArcPB.cs
//! \date       Sat Mar 11 11:41:03 2017
//! \brief      KAAS engine audio archive.
//
// Copyright (C) 2015-2017 by morkt
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

namespace GameRes.Formats.KAAS
{
    [Export(typeof(ArchiveFormat))]
    public class PbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PB"; } }
        public override string Description { get { return "KAAS engine audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.EndsWith (".pb", StringComparison.InvariantCultureIgnoreCase))
                return null;
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfff)
                return null;
            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            bool is_voice = VFS.IsPathEqualsToFileName (file.Name, "voice.pb");
            int data_offset = index_offset + 8 * count;
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                Entry entry;
                if (!is_voice)
                    entry = new Entry { Name = i.ToString ("D4"), Type = "audio", Offset = offset };
                else
                    entry = new Entry { Name = string.Format ("{0:D4}.pb", i), Type = "archive", Offset = offset };
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
