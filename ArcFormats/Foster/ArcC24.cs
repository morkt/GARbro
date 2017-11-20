//! \file       ArcC24.cs
//! \date       Sun Feb 19 14:21:21 2017
//! \brief      Foster's game engine multi-frame image.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Foster
{
    [Export(typeof(ArchiveFormat))]
    public class C24Opener : ArchiveFormat
    {
        public override string         Tag { get { return "C24"; } }
        public override string Description { get { return "Foster game engine multi-image"; } }
        public override uint     Signature { get { return 0x00343243; } } // 'C24'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            uint index_offset = 8;
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                if (offset > 0 && offset <= file.MaxOffset)
                {
                    var entry = new Entry
                    {
                        Name = string.Format ("{0}@{1:D4}", base_name, i),
                        Type = "image",
                        Offset = offset,
                    };
                    dir.Add (entry);
                }
            }
            dir.Sort ((a, b) => (int)(a.Offset - b.Offset));
            for (int i = 1; i < dir.Count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            }
            var last_entry = dir[dir.Count-1];
            last_entry.Size = (uint)(file.MaxOffset - last_entry.Offset);
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var info = ReadImageInfo (arc.File, entry.Offset, 24);
            var input = arc.File.CreateStream (0, (uint)arc.File.MaxOffset);
            return new C24Decoder (input, info);
        }

        internal C24MetaData ReadImageInfo (ArcView file, long offset, int bpp)
        {
            return new C24MetaData
            {
                Width   = file.View.ReadUInt32 (offset),
                Height  = file.View.ReadUInt32 (offset+4),
                OffsetX = file.View.ReadInt32 (offset+8),
                OffsetY = file.View.ReadInt32 (offset+12),
                BPP     = bpp,
                DataOffset = (uint)(offset + 0x10),
            };
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class C25Opener : C24Opener
    {
        public override string         Tag { get { return "C25"; } }
        public override uint     Signature { get { return 0x00353243; } } // 'C25'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var info = ReadImageInfo (arc.File, entry.Offset, 32);
            var input = arc.File.CreateStream (0, (uint)arc.File.MaxOffset);
            return new C25Decoder (input, info);
        }
    }
}
