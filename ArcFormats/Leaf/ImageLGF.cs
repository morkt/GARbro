//! \file       ImageLGF.cs
//! \date       Sat Sep 03 15:03:06 2016
//! \brief      Leaf image format.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ImageFormat))]
    public class LgfFormat : ImageFormat
    {
        public override string         Tag { get { return "LGF"; } }
        public override string Description { get { return "Leaf image format"; } }
        public override uint     Signature { get { return 0; } }

        public LgfFormat ()
        {
            // fourth byte is BPP
            Signatures = new uint[] { 0x1866676C, 0x2066676C, 0x0966676C };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[8];
            if (8 != stream.Read (header, 0, 8))
                return null;
            int bpp = header[3];
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt16 (header, 4),
                Height  = LittleEndian.ToUInt16 (header, 6),
                BPP     = 9 == bpp ? 8 : bpp,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            int stride = (int)info.Width * info.BPP / 8;
            var pixels = new byte[stride * (int)info.Height];
            stream.Position = 12;
            BitmapPalette palette = null;
            if (8 == info.BPP)
                palette = ReadPalette (stream);
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24
                               : 32 == info.BPP ? PixelFormats.Bgr32
                               : PixelFormats.Indexed8;
            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LgfFormat.Write not implemented");
        }

        BitmapPalette ReadPalette (Stream input)
        {
            var palette_data = new byte[0x400];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new EndOfStreamException();
            var palette = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c], palette_data[c+1], palette_data[c+2]);
            }
            return new BitmapPalette (palette);
        }
    }
}
