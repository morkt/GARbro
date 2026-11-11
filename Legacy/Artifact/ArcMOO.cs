//! \file       ArcMOO.cs
//! \date       2026-08-18
//! \brief      Moo Ichiza resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Artifact
{
    [Export(typeof(ArchiveFormat))]
    public class MooOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MOO"; } }
        public override string Description { get { return "Moo Ichiza resource archive"; } }
        public override uint     Signature { get { return 0x446F6F4D; } } // 'MooData'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public MooOpener ()
        {
            Signatures = new uint[] { 0x446F6F4D, 0x00000010 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset;
            if (file.View.AsciiEqual (0, "MooData"))
                base_offset = 0x24;
            else
                base_offset = 0x18;
            int count = file.View.ReadInt32 (base_offset) + 1;
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);

            uint index_offset = base_offset + 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset = file.View.ReadUInt32 (index_offset),
                };
                dir.Add (entry);
                index_offset += 4;
            }
            for (int i = 0; i < count; ++i)
            {
                long size;
                if (i == count - 1)
                    size = file.MaxOffset - dir[i].Offset - index_offset;
                else
                    size = dir[i+1].Offset - dir[i].Offset;
                if (size < 0)
                    return null;
                dir[i].Offset += index_offset;
                dir[i].Size = (uint)size;
                if (!dir[i].CheckPlacement (file.MaxOffset))
                    return null;
                var sig = file.View.ReadBytes (dir[i].Offset, 4);
                if (BitConverter.ToUInt32 (sig, 0) == 0x53535A4C)
                    dir[i].Type = "image";
            }
            return new ArcFile (file, this, dir);
        }
    }
}
