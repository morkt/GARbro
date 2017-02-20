//! \file       ArcCCF.cs
//! \date       Sun Feb 19 22:05:59 2017
//! \brief      Black Rainbow audio archive.
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

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class CcfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CCF"; } }
        public override string Description { get { return "BlackRainbow audio archive"; } }
        public override uint     Signature { get { return 0x22664343; } } // 'CCf"'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CcfOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 8;
            long base_offset = index_offset + count * 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = AutoEntry.Create (file, base_offset+offset, name);
                if (entry.Offset >= file.MaxOffset)
                    return null;
                dir.Add (entry);
            }
            for (int i = 1; i < dir.Count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            }
            var last_entry = dir[dir.Count-1];
            last_entry.Size = (uint)(file.MaxOffset - last_entry.Offset);
            return new ArcFile (file, this, dir);
        }
    }
}
