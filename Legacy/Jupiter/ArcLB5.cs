//! \file       ArcLB5.cs
//! \date       2018 Jan 25
//! \brief      Jupiter resource archive.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.Jupiter
{
    [Export(typeof(ArchiveFormat))]
    public class Lb5Opener : ArchiveFormat
    {
        public override string         Tag { get { return "LB5"; } }
        public override string Description { get { return "Jupiter resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".lb5"))
                return null;
            var idx_name = Path.ChangeExtension (file.Name, "idx");
            if (!VFS.FileExists (idx_name))
                return null;
            using (var index = VFS.OpenView (idx_name))
            {
                int count = index.View.ReadInt32 (0);
                if (!IsSaneCount (count))
                    return null;
                uint idx_offset = 4;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.View.ReadUInt32 (idx_offset);
                    uint size   = index.View.ReadUInt32 (idx_offset + 4);
                    var name = index.View.ReadString (idx_offset + 9, 0xF);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    idx_offset += 0x18;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (arc.File.View.ReadUInt32 (entry.Offset) != 0x88888888 ||
                arc.File.View.ReadUInt16 (entry.Offset+0x10) != 0x4D42) // 'BM'
                return base.OpenImage (arc, entry);
            var info = new Lb5MetaData {
                Width  = arc.File.View.ReadUInt32 (entry.Offset+4),
                Height = arc.File.View.ReadUInt32 (entry.Offset+8),
                UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset+0xC),
                BPP = arc.File.View.ReadUInt16 (entry.Offset+0x1C),
            };
            var input = arc.File.CreateStream (entry.Offset+0x10, entry.Size-0x10);
            return new Lb5ImageDecoder (input, info);
        }
    }

    internal class Lb5MetaData : ImageMetaData
    {
        public uint UnpackedSize;
    }

    internal class Lb5ImageDecoder : BinaryImageDecoder
    {
        byte[]  m_output;

        public Lb5ImageDecoder (IBinaryStream input, Lb5MetaData info) : base (input, info)
        {
            m_output = new byte[info.UnpackedSize];
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x36;
            int block_count = ((int)Info.Width + 7) / 8 * ((int)Info.Height + 7) / 8;
            var block_info = m_input.ReadBytes (block_count);
            throw new NotImplementedException();
        }
    }
}
