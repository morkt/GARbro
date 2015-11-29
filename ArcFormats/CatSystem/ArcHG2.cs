//! \file       ArcHG2.cs
//! \date       Sun Nov 29 13:09:09 2015
//! \brief      CatSystem2 HG-2 multi-frame image.
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
    public class Hg2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "HG2"; } }
        public override string Description { get { return "CatSystem2 engine multi-image"; } }
        public override uint     Signature { get { return 0x322d4748; } } // 'HG-2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x25 != file.View.ReadInt32 (8))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            long offset = 0xC;
            int i = 0;
            while (offset < file.MaxOffset)
            {
                uint section_size = file.View.ReadUInt32 (offset+0x40);
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D4}.tga", base_name, i),
                    Type = "image",
                    Offset = offset,
                };
                if (0 == section_size)
                    entry.Size = (uint)(file.MaxOffset - offset);
                else
                    entry.Size = section_size;
                dir.Add (entry);
                if (0 == section_size)
                    break;
                offset += section_size;
                ++i;
            }
            if (dir.Count < 1)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            // emulate TGA image
            var offset = entry.Offset;
            var info = new Hg2MetaData
            {
                HeaderSize  = 0x4C,
                Width       = arc.File.View.ReadUInt32 (offset),
                Height      = arc.File.View.ReadUInt32 (offset+4),
                BPP         = arc.File.View.ReadInt32 (offset+8),
                DataPacked  = arc.File.View.ReadInt32 (offset+0x14),
                DataUnpacked= arc.File.View.ReadInt32 (offset+0x18),
                CtlPacked   = arc.File.View.ReadInt32 (offset+0x1C),
                CtlUnpacked = arc.File.View.ReadInt32 (offset+0x20),
                CanvasWidth = arc.File.View.ReadUInt32 (offset+0x2C),
                CanvasHeight= arc.File.View.ReadUInt32 (offset+0x30),
                OffsetX     = arc.File.View.ReadInt32 (offset+0x34),
                OffsetY     = arc.File.View.ReadInt32 (offset+0x38),
            };
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var reader = new Hg2Reader (input, info))
            {
                return TgaStream.Create (info, reader.Unpack(), true);
            }
        }
    }
}
