//! \file       ImageTHP.cs
//! \date       2019 Feb 28
//! \brief      PrimeSoft image format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Prime
{
    [Export(typeof(ImageFormat))]
    public class ThpFormat : ImageFormat
    {
        public override string         Tag { get { return "THP"; } }
        public override string Description { get { return "Prime Soft image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("THP"))
                return null;
            var header = file.ReadHeader (4);
            ushort width  = header.ToUInt16 (0);
            ushort height = header.ToUInt16 (0);
            if (width == 0 || width > 0x4000 || height == 0 || height > 0x4000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            var palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.Bgr);
            int stride = (info.iWidth + 3) & ~3;
            var pixels = new byte[info.iHeight * stride];
            int length = info.iHeight * info.iWidth;
            int dst = 0;
            while (dst < length)
            {
                byte px = file.ReadUInt8();
                int next = file.PeekByte();
                if (px == next)
                {
                    file.ReadByte();
                    int count = file.ReadByte() + 1;
                    while (count --> 0)
                    {
                        pixels[dst++] = px;
                    }
                }
                else
                    pixels[dst++] = px;
            }
            return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ThpFormat.Write not implemented");
        }
    }
}
