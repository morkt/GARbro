//! \file       ArcANM.cs
//! \date       Sat Jan 23 04:23:39 2016
//! \brief      KaGuYa script engine animation resource.
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

namespace GameRes.Formats.Kaguya
{
    internal class AnmArchive : ArcFile
    {
        public readonly ImageMetaData   ImageInfo;

        public AnmArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData base_info)
            : base (arc, impl, dir)
        {
            ImageInfo = base_info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class AnmOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANM/KAGUYA"; } }
        public override string Description { get { return "KaGuYa script engine animation resource"; } }
        public override uint     Signature { get { return 0x30304E41; } } // 'AN00'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public AnmOpener ()
        {
            Extensions = new string[] { "anm" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int frame_count = file.View.ReadInt16 (0x14);
            uint current_offset = 0x18 + (uint)frame_count * 4;
            int count = file.View.ReadInt16 (current_offset);
            if (!IsSaneCount (count))
                return null;
            var base_info = new ImageMetaData
            {
                OffsetX     = file.View.ReadInt32 (4),
                OffsetY     = file.View.ReadInt32 (8),
                Width       = file.View.ReadUInt32 (0x0C),
                Height      = file.View.ReadUInt32 (0x10),
                BPP         = 32,
            };
            current_offset += 2;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint width  = file.View.ReadUInt32 (current_offset+8);
                uint height = file.View.ReadUInt32 (current_offset+12);
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D2}.tga", base_name, i),
                    Type = "image",
                    Offset = current_offset,
                    Size = 0x10 + 4*width*height,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new AnmArchive (file, this, dir, base_info);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var base_info = ((AnmArchive)arc).ImageInfo;
            // emulate TGA image
            var offset = entry.Offset;
            var info = new ImageMetaData
            {
                OffsetX     = base_info.OffsetX + arc.File.View.ReadInt32 (offset),
                OffsetY     = base_info.OffsetY + arc.File.View.ReadInt32 (offset+4),
                Width       = arc.File.View.ReadUInt32 (offset+8),
                Height      = arc.File.View.ReadUInt32 (offset+12),
                BPP         = 32,
            };
            offset += 0x10;
            var pixels = arc.File.View.ReadBytes (offset, 4*info.Width*info.Height);
            return TgaStream.Create (info, pixels, true);
        }
    }
}
