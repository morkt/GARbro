//! \file       ArcTAN.cs
//! \date       2018 Jan 16
//! \brief      D.O. animation resource format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;


namespace GameRes.Formats.Ikura
{
    internal class TanEntry : Entry
    {
        public int  Index;
    }

    internal class TanArchive : ArcFile
    {
        public readonly TanMetaData Info;

        public TanArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, TanMetaData info)
            : base (arc, impl, dir)
        {
            Info = info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class TanOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TAN/DO"; } }
        public override string Description { get { return "D.O. animation resource"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".tan"))
                return null;
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_pos = 2 + (uint)count * 4;
            var info = new TanMetaData {
                Width  = file.View.ReadUInt16 (index_pos),
                Height = file.View.ReadUInt16 (index_pos+2),
                BPP    = 8,
                DataOffset = index_pos + 4,
            };
            index_pos += 0x404;
            count = file.View.ReadInt16 (index_pos);
            if (!IsSaneCount (count))
                return null;
            index_pos += 2;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var base_offset = index_pos + 4 * count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new TanEntry {
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = base_offset + file.View.ReadUInt32 (index_pos),
                    Index = i,
                };
                dir.Add (entry);
                index_pos += 4;
            }
            for (int i = 1; i < count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
                if (!dir[i-1].CheckPlacement (file.MaxOffset))
                    return null;
            }
            dir[dir.Count-1].Size = (uint)(file.MaxOffset - dir[dir.Count-1].Offset);
            return new TanArchive (file, this, dir, info);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var tarc = (TanArchive)arc;
            var tent = (TanEntry)entry;
            var input = arc.File.CreateStream();
            return new TanFrameDecoder (input, tarc.Info, tent.Index);
        }
    }

    internal class TanFrameDecoder : BinaryImageDecoder
    {
        int         m_frame;
        TanReader   m_reader;

        public TanFrameDecoder (IBinaryStream input, TanMetaData info, int frame)
            : base (input, info)
        {
            m_frame = frame;
            m_reader = new TanReader (m_input, info);
        }

        protected override ImageData GetImageData ()
        {
            var pixels = m_reader.UnpackFrame (m_frame);
            return ImageData.Create (Info, m_reader.Format, m_reader.Palette, pixels);
        }
    }
}
