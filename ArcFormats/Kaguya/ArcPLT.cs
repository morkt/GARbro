//! \file       ArcPLT.cs
//! \date       2022 May 03
//! \brief      KaGuYa script engine animation resource.
//
// Copyright (C) 2022 by morkt
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
using System.Windows.Media;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ArchiveFormat))]
    public class Pl00Opener : AnmOpenerBase
    {
        public override string         Tag { get { return "PLT/KAGUYA"; } }
        public override uint     Signature { get { return 0x30304C50; } } // 'PL00'

        public Pl00Opener ()
        {
            Extensions = new string[] { "plt" };
        }

        public override List<Entry> GetFramesList (IBinaryStream file)
        {
            file.Position = 4;
            int count = file.ReadInt16();
            if (!IsSaneCount (count))
                return null;
            file.Position = 0x16;
            var current_offset = file.Position;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.Position = current_offset + 8;
                uint width  = file.ReadUInt32();
                uint height = file.ReadUInt32();
                uint depth  = file.ReadUInt32();
                uint image_size = depth*width*height;
                var entry = new AnmEntry
                {
                    Offset = current_offset,
                    Size = 0x14 + image_size,
                    ImageDataOffset = current_offset + 0x14,
                    ImageDataSize = image_size,
                };
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return dir;
        }

        public override IImageDecoder CreateDecoder (IBinaryStream input, ImageMetaData info)
        {
            return new Pl00Decoder (input, info);
        }
    }

    internal class Pl00Decoder : BinaryImageDecoder
    {
        public Pl00Decoder (IBinaryStream input, ImageMetaData base_info) : base (input)
        {
            Info = new ImageMetaData
            {
                OffsetX = base_info.OffsetX + m_input.ReadInt32(),
                OffsetY = base_info.OffsetY + m_input.ReadInt32(),
                Width   = m_input.ReadUInt32(),
                Height  = m_input.ReadUInt32(),
                BPP     = m_input.ReadInt32() * 8,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x14;
            int stride = Info.BPP * Info.iWidth / 8;
            var pixels = m_input.ReadBytes (stride*Info.iHeight);
            PixelFormat format = 24 == Info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.CreateFlipped (Info, format, null, pixels, stride);
        }
    }
}
