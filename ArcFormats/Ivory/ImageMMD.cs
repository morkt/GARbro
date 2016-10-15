//! \file       ImageMMD.cs
//! \date       Thu Sep 08 18:23:14 2016
//! \brief      Ivory compressed image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ivory
{
    internal class MmdMetaData : ImageMetaData
    {
        public int  Colors;
        public int  Size1;
        public int  Size2;
        public int  Size3;
    }

    [Export(typeof(ImageFormat))]
    public class MmdFormat : ImageFormat
    {
        public override string         Tag { get { return "MOE/MMD"; } }
        public override string Description { get { return "Ivory image format"; } }
        public override uint     Signature { get { return 0x1A444D4D; } } // 'MMD'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            var info = new MmdMetaData
            {
                Width   = header.ToUInt16 (4),
                Height  = header.ToUInt16 (6),
                BPP     = 8,
                Size1   = header.ToInt32 (8),
                Size2   = header.ToInt32 (0x0C),
                Size3   = header.ToInt32 (0x10),
                Colors  = header.ToInt32 (0x14),
            };
            if (info.Size1 <= 0 || info.Size2 <= info.Size1 || info.Size3 <= 0)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream input, ImageMetaData info)
        {
            var meta = (MmdMetaData)info;
            var pixels = new byte[info.Width * info.Height];
            input.Position = 0x18;
            var buf1 = input.ReadBytes (meta.Size1);
            var buf2 = input.ReadBytes (meta.Size2 - meta.Size1);
            int w = (int)info.Width / 4;
            var line = new byte[w];
            int mask = 0x80;
            int b1 = 0;
            int b2 = 0;
            int dst = 0;
            for (int y = (int)info.Height; y > 0; --y)
            {
                for (int x = 0; x < w; ++x)
                {
                    if (0 != (mask & buf1[b1]))
                    {
                        line[x] ^= buf2[b2++];
                    }
                    mask >>= 1;
                    if (0 == mask)
                    {
                        mask = 0x80;
                        ++b1;
                    }
                    byte p = line[x];
                    int q = p >> 4;
                    for (int j = 0; j < 2; ++j)
                    {
                        if (0 != q)
                        {
                            int offset = ShiftTable[q + 16] + (int)info.Width * ShiftTable[q];
                            int src = dst - offset;
                            pixels[dst++] = pixels[src];
                            pixels[dst++] = pixels[src+1];
                        }
                        else
                        {
                            input.Read (pixels, dst, 2);
                            dst += 2;
                        }
                        q = p & 0xF;
                    }
                }
            }
            input.Position = 0x18 + meta.Size2 + meta.Size3;
            var palette = ReadPalette (input.AsStream, meta.Colors);
            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
        }

        static readonly byte[] ShiftTable = {
            0, 0, 0, 0, 1, 1, 2, 2, 2, 4, 4, 4, 8, 8, 8, 16,
            0, 2, 4, 8, 0, 2, 0, 2, 4, 0, 2, 4, 0, 2, 4, 0,
        };

        BitmapPalette ReadPalette (Stream input, int colors)
        {
            int palette_size = colors * 3;
            var palette_data = new byte[Math.Max (palette_size, 0x300)];
            if (palette_size != input.Read (palette_data, 0, palette_size))
                throw new EndOfStreamException();
            var palette = new Color[0x100];
            if (colors > 0x100)
                colors = 0x100;
            int src = 0;
            for (int i = 0; i < palette.Length; ++i)
            {
                byte r = palette_data[src++];
                byte g = palette_data[src++];
                byte b = palette_data[src++];
                palette[i] = Color.FromRgb (r, g, b);
            }
            return new BitmapPalette (palette);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MmdFormat.Write not implemented");
        }
    }
}
