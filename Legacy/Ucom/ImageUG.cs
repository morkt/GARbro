//! \file       ImageUG.cs
//! \date       2023 Oct 16
//! \brief      Ucom image format (PC-98).
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [961220][Ucom] Bunkasai

namespace GameRes.Formats.Ucom
{
    [Export(typeof(ImageFormat))]
    public class UgFormat : ImageFormat
    {
        public override string         Tag => "UG";
        public override string Description => "Ucom image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".UG"))
                return null;
            int left   = file.ReadUInt16();
            int top    = file.ReadUInt16();
            int right  = file.ReadUInt16();
            int bottom = file.ReadUInt16();
            int width = (right - left + 1) << 3;
            int height = bottom - top + 1;
            if (width <= 0 || height <= 0 || width > 640 || height > 512)
                return null;
            return new ImageMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = left,
                OffsetY = top,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new UgReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("UgFormat.Write not implemented");
        }
    }

    internal class UgReader : System98.GraBaseReader
    {
        public UgReader (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        public ImageData Unpack ()
        {
            m_input.Position = 8;
            var palette = ReadPalette();
            UnpackBits();
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, Pixels, Stride);
        }

        BitmapPalette ReadPalette ()
        {
            var colors = new Color[16];
            for (int i = 0; i < 16; ++i)
            {
                ushort rgb = m_input.ReadUInt16();
                int b = (rgb & 0xF) * 0x11;
                int r = ((rgb >> 4) & 0xF) * 0x11;
                int g = ((rgb >> 8) & 0xF) * 0x11;
                colors[i] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
            }
            return new BitmapPalette (colors);
        }
    }
}
