//! \file       ImageBET.cs
//! \date       2017 Dec 09
//! \brief      System21 compressed image.
//
// Copyright (C) 2017 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.System21
{
    [Export(typeof(ImageFormat))]
    public class BetFormat : ImageFormat
    {
        public override string         Tag { get { return "BET"; } }
        public override string Description { get { return "System21 compressed image format"; } }
        public override uint     Signature { get { return 0x44445A53; } } // 'SZDD'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bet"))
                return null;
            using (var lz = OpenLzStream (file.AsStream))
            using (var input = new BinaryStream (lz, file.Name))
            {
                uint w = input.ReadUInt32();
                uint h = input.ReadUInt32();
                int bpp = input.ReadUInt16();
                if ((bpp != 24 && bpp != 8) || 0 == w || w > 0x8000 || 0 == h || h > 0x8000)
                    return null;
                return new ImageMetaData { Width = w, Height = h, BPP = bpp };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenLzStream (file.AsStream))
            {
                int stride = (int)info.Width * info.BPP / 8;
                var pixels = new byte[stride * (int)info.Height];
                for (int i = 0; i < 10; ++i)
                    input.ReadByte();
                BitmapPalette palette = null;
                if (8 == info.BPP)
                    palette = ReadPalette (input);
                if (input.Read (pixels, 0, pixels.Length) != pixels.Length)
                    throw new InvalidFormatException();
                for (int i = 0; i < pixels.Length; ++i)
                    pixels[i] ^= 0xFF;
                PixelFormat format = 8 == info.BPP ? PixelFormats.Indexed8 : PixelFormats.Bgr24;
                return ImageData.CreateFlipped (info, format, palette, pixels, stride);
            }
        }

        Stream OpenLzStream (Stream input)
        {
            input.Position = 0xE;
            var lz = new LzssStream (input, LzssMode.Decompress, true);
            lz.Config.FrameSize = 0x1000;
            lz.Config.FrameFill = 0x20;
            lz.Config.FrameInitPos = 0x1000 - 0x10;
            return lz;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BetFormat.Write not implemented");
        }
    }
}
