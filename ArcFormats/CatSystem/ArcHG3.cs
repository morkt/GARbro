//! \file       ArcHG3.cs
//! \date       Mon Oct 12 22:34:11 2015
//! \brief      CatSystem2 image resource.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.CatSystem
{
    [Export(typeof(ArchiveFormat))]
    public class Hg3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "HG3"; } }
        public override string Description { get { return "CatSystem2 engine multi-image"; } }
        public override uint     Signature { get { return 0x332d4748; } } // 'HG-3'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            long offset = 0xC;
            int i = 0;
            while (offset+0x14 < file.MaxOffset && file.View.AsciiEqual (offset+8, "stdinfo"))
            {
                uint section_size = file.View.ReadUInt32 (offset);
                if (0 == section_size)
                    section_size = (uint)(file.MaxOffset - offset);
                uint stdinfo_size = file.View.ReadUInt32 (offset+0x10);
                if (file.View.AsciiEqual (offset+8+stdinfo_size, "img"))
                {
                    var entry = new Entry
                    {
                        Name = string.Format ("{0}#{1:D4}", base_name, i),
                        Type = "image",
                        Offset = offset + 8,
                        Size = section_size - 8,
                    };
                    dir.Add (entry);
                }
                offset += section_size;
                ++i;
            }
            if (dir.Count < 1)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var offset = entry.Offset+8;
            var info = new HgMetaData
            {
                HeaderSize = arc.File.View.ReadUInt32 (offset),
                Width   = arc.File.View.ReadUInt32 (offset+8),
                Height  = arc.File.View.ReadUInt32 (offset+0xC),
                BPP     = arc.File.View.ReadInt32 (offset+0x10),
                OffsetX = arc.File.View.ReadInt32 (offset+0x14),
                OffsetY = arc.File.View.ReadInt32 (offset+0x18),
                CanvasWidth = arc.File.View.ReadUInt32 (offset+0x1C),
                CanvasHeight = arc.File.View.ReadUInt32 (offset+0x20),
            };
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new Hg3Reader (input, info);
        }
    }
}
