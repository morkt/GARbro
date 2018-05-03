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

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".chr"))
                return null;
            int count = (int)(file.MaxOffset / 0x96000);
            if (!IsSaneCount (count) || count * 0x96000 != file.MaxOffset)
                return null;

            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = i.ToString ("D5"),
                    Type = "image",
                    Offset = offset,
                    Size = 0x96000,
                };
                dir.Add (entry);
                offset += 0x96000;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ChrImageDecoder (input);
        }
    }

    internal class ChrImageDecoder : BinaryImageDecoder
    {
        public ChrImageDecoder (IBinaryStream input) : base (input)
        {
            Info = new ImageMetaData { Width = 640, Height = 480, BPP = 16 };
        }

        protected override ImageData GetImageData ()
        {
            var pixels = m_input.ReadBytes (0x96000);
            return ImageData.CreateFlipped (Info, PixelFormats.Bgr555, null, pixels, 640*2);
        }
    }
}
