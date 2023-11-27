//! \file       ImageHMP.cs
//! \date       2022 Jun 16
//! \brief      Candy Soft hover map.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Interheart
{
    [Export(typeof(ImageFormat))]
    public class HmpFormat : ImageFormat
    {
        public override string         Tag { get { return "HMP"; } }
        public override string Description { get { return "Interheart hover map"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".hmp"))
                return null;
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            if (width == 0 || width > 0x7FFF || height == 0 || height > 0x7FFF)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            var pixels = file.ReadBytes (info.iWidth * info.iHeight);
            return ImageData.Create (info, PixelFormats.Indexed8, DefaultPalette, pixels);
//            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HmpFormat.Write not implemented");
        }

        static readonly BitmapPalette DefaultPalette = new BitmapPalette (GenerateColors());

        static Color[] GenerateColors ()
        {
            var colors = new Color[256];
            for (int i = 8; i < 256; ++i)
            {
                colors[i] = Color.FromRgb ((byte)i, (byte)i, (byte)i);
            }
            colors[0] = Color.FromRgb (0x00, 0x00, 0x00);
            colors[1] = Color.FromRgb (0x00, 0x00, 0x7F);
            colors[2] = Color.FromRgb (0x00, 0x7F, 0x00);
            colors[3] = Color.FromRgb (0x00, 0x7F, 0x7F);
            colors[4] = Color.FromRgb (0x7F, 0x00, 0x00);
            colors[5] = Color.FromRgb (0x7F, 0x00, 0x7F);
            colors[6] = Color.FromRgb (0x7F, 0x7F, 0x00);
            colors[7] = Color.FromRgb (0x7F, 0x7F, 0x7F);
            colors[8] = Color.FromRgb (0x00, 0x00, 0xFF);
            colors[9] = Color.FromRgb (0x00, 0xFF, 0x00);
            colors[10] = Color.FromRgb (0x00, 0xFF, 0xFF);
            colors[11] = Color.FromRgb (0xFF, 0x00, 0x00);
            colors[12] = Color.FromRgb (0xFF, 0x00, 0xFF);
            colors[13] = Color.FromRgb (0xFF, 0xFF, 0x00);
            colors[14] = Color.FromRgb (0xFF, 0xFF, 0xFF);
            colors[15] = Color.FromRgb (0xFF, 0x00, 0x7F);
            colors[16] = Color.FromRgb (0xFF, 0x7F, 0x00);
            colors[16] = Color.FromRgb (0xFF, 0x7F, 0x7F);
            colors[17] = Color.FromRgb (0x7F, 0x00, 0xFF);
            colors[18] = Color.FromRgb (0xFF, 0xFF, 0x7F);
            return colors;
        }
    }
}
