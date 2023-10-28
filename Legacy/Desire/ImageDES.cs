//! \file       ImageDES.cs
//! \date       2023 Oct 15
//! \brief      DES98 engine image format (PC-98).
//
// Copyright (C) 2023 by morkt
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [940720][Desire] H+

namespace GameRes.Formats.Desire
{
    [Export(typeof(ImageFormat))]
    public class DesFormat : ImageFormat
    {
        public override string         Tag => "DES98";
        public override string Description => "Des98 engine image format";
        public override uint     Signature => 0;

        public DesFormat ()
        {
            Extensions = new[] { "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            ushort width  = Binary.BigEndian (file.ReadUInt16());
            ushort height = Binary.BigEndian (file.ReadUInt16());
            if (0 == width || 0 == height || (width & 7) != 0 || width > 640 || height > 400)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            var palette = ReadPalette (file.AsStream, 16, PaletteFormat.Rgb);
            var reader = new System98.GraBaseReader (file, info);
            reader.UnpackBits();
            return ImageData.Create (info, PixelFormats.Indexed4, palette, reader.Pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DesFormat.Write not implemented");
        }
    }
}
