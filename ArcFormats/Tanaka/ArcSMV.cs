//! \file       ArcSMV.cs
//! \date       Wed Feb 01 09:03:04 2017
//! \brief      Animation resource by Tanaka Tatsuhiro.
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
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class SmvOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SMV"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine animation resource"; } }
        public override uint     Signature { get { return 0x31564D53; } } // 'SMV1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt32 (4) != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0x40;
            uint header_size = file.View.ReadUInt32 (index_offset);
            int bpp = file.View.ReadInt32 (index_offset+0xE);
            if (bpp != 8)
                return null;
            index_offset += header_size + 4;
            var palette = ImageFormat.ReadPalette (file, index_offset);
            index_offset += 0x400;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "image",
                    Offset = file.View.ReadUInt32 (index_offset),
                    Size = file.View.ReadUInt32 (index_offset+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new SmvArchive (file, this, dir, palette);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "TX04"))
                throw new InvalidFormatException();
            var smv = (SmvArchive)arc;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new SmvFrameDecoder (input, smv.Palette);
        }
    }

    internal class SmvFrameDecoder : BinaryImageDecoder
    {
        readonly BitmapPalette Palette;

        public SmvFrameDecoder (IBinaryStream input, BitmapPalette palette) : base (input)
        {
            Palette = palette;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 4;
            var info = new TxMetaData { BPP = 8, Colors = 0x100 };
            this.Info = info;
            info.Stride = m_input.ReadUInt16();
            info.Height = m_input.ReadUInt16();
            info.Width  = (uint)info.Stride;
            info.DataOffset = m_input.Position;
            var reader = new TxReader (m_input, info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (Info, PixelFormats.Indexed8, Palette, pixels, info.Stride);
        }

        public static void BlendFrames (byte[] key_frame, byte[] overlay)
        {
            for (int i = 0; i < overlay.Length; ++i)
            {
                if (0xFF != overlay[i])
                    key_frame[i] = overlay[i];
            }
        }
    }

    internal class SmvArchive : ArcFile
    {
        public readonly BitmapPalette   Palette;

        public SmvArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, BitmapPalette palette)
            : base (arc, impl, dir)
        {
            Palette = palette;
        }
    }
}
