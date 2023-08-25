//! \file       ArcCHR.cs
//! \date       2018 Apr 08
//! \brief      Airyu archive format.
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
using System.Windows.Media;

namespace GameRes.Formats.Airyu
{
    [Export(typeof(ArchiveFormat))]
    public class ChrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CHR/AIRYU"; } }
        public override string Description { get { return "Airyu resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly uint[] ImageSizes = new[] { 0x96000u, 0x4B000u, 0x19000u };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".chr"))
                return null;
            int count = 0;
            uint image_size = 1;
            for (int i = 0; i < ImageSizes.Length; ++i)
            {
                image_size = ImageSizes[i];
                count = (int)(file.MaxOffset / image_size);
                if (IsSaneCount (count) && count * image_size == file.MaxOffset)
                    break;
                count = 0;
            }
            if (0 == count)
                return null;

            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = i.ToString ("D5"),
                    Type = "image",
                    Offset = offset,
                    Size = image_size,
                };
                dir.Add (entry);
                offset += image_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ChrImageDecoder (input, entry.Size);
        }
    }

    internal class ChrImageDecoder : BinaryImageDecoder
    {
        int     m_image_size;
        int     m_stride;

        public ChrImageDecoder (IBinaryStream input, uint size) : base (input)
        {
            m_image_size = (int)size;
            switch (size)
            {
            case 0x19000: Info = new ImageMetaData { Width = 200, Height = 256, BPP = 16 }; break;
            case 0x4B000: Info = new ImageMetaData { Width = 320, Height = 480, BPP = 16 }; break;
            case 0x96000: Info = new ImageMetaData { Width = 640, Height = 480, BPP = 16 }; break;
            default: throw new InvalidFormatException ("Invalid image size.");
            }
            m_stride = Info.iWidth * 2;
        }

        protected override ImageData GetImageData ()
        {
            var pixels = m_input.ReadBytes (m_image_size);
            return ImageData.CreateFlipped (Info, PixelFormats.Bgr555, null, pixels, m_stride);
        }
    }
}
