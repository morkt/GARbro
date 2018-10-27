//! \file       ArcCP3.cs
//! \date       2018 Oct 26
//! \brief      Seraphim engine multi-frame image.
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
using System.Windows.Media;

namespace GameRes.Formats.Seraphim
{
    [Export(typeof(ArchiveFormat))]
    public class Cp3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CP3"; } }
        public override string Description { get { return "Seraphim engine multi-frame image"; } }
        public override uint     Signature { get { return 0x58335043; } } // 'CP3X'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            uint offset = 0x2C;
            for (int i = 0; i < count; ++i)
            {
                uint width  = file.View.ReadUInt32 (offset+8);
                uint height = file.View.ReadUInt32 (offset+12);
                uint size = width * height * 4 + 0x10;
                if (size != 0x10)
                {
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1:D4}", base_name, i),
                        Type = "image",
                        Offset = offset,
                        Size = size,
                    };
                    dir.Add (entry);
                }
                offset += size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new Cp3ImageDecoder (input);
        }
    }

    internal class Cp3ImageDecoder : BinaryImageDecoder
    {
        public Cp3ImageDecoder (IBinaryStream input) : base (input)
        {
            var header = input.ReadHeader (0x10);
            Info = new ImageMetaData {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (12),
                BPP    = 32,
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            int stride = (int)Info.Width * 4;
            var pixels = m_input.ReadBytes (stride * (int)Info.Height);
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, stride);
        }
    }
}
