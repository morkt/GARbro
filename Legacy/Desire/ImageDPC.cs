//! \file       ImageDPC.cs
//! \date       2023 Oct 15
//! \brief      Desire image format (PC-98).
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [970801][Desire] Yuugiri ~Ningyoushi no Isan~

namespace GameRes.Formats.Desire
{
    [Export(typeof(ImageFormat))]
    public class DpcFormat : ImageFormat
    {
        public override string         Tag => "DPC";
        public override string Description => "Desire image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".DPC"))
                return null;
            file.Position = 0x20;
            short left    = file.ReadInt16();
            short top     = file.ReadInt16();
            ushort width  = file.ReadUInt16();
            ushort height = file.ReadUInt16();
            if (0 == width || 0 == height || left < 0 || left + width > 2048 || top < 0 || top + height > 2048)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                OffsetX = left,
                OffsetY = top,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var palette = ReadPalette (file);
            file.Position = 0x28;
            var reader = new System98.GraBaseReader (file, info);
            reader.UnpackBits();
            return ImageData.Create (info, PixelFormats.Indexed4, palette, reader.Pixels, reader.Stride);
        }

        BitmapPalette ReadPalette (IBinaryStream input)
        {
            var colors = new Color[16];
            for (int i = 0; i < 16; ++i)
            {
                ushort w = input.ReadUInt16();
                int alpha = (w & 1) == 0 ? 0xFF : 0;
                int g = (w >> 12) * 0x11;
                int r = ((w >> 7) & 0xF) * 0x11;
                int b = ((w >> 2) & 0xF) * 0x11;

                colors[i] = Color.FromArgb ((byte)alpha, (byte)r, (byte)g, (byte)b);
            }
            return new BitmapPalette (colors);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DpcFormat.Write not implemented");
        }
    }
}
