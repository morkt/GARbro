//! \file       ImageMRL.cs
//! \date       2019 May 22
//! \brief      ADVG Script Interpreter System image format.
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Artel
{
    internal class MrlMetaData : ImageMetaData
    {
        public bool HasAlpha;
    }

    [Export(typeof(ImageFormat))]
    public class MrlFormat : ImageFormat
    {
        public override string         Tag { get { return "MRL"; } }
        public override string Description { get { return "Artel ADVG engine image format"; } }
        public override uint     Signature { get { return 0x524D754D; } } // 'MuMRL'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (header[4] != 'L')
                return null;
            int bpp = header.ToUInt16 (0xC) * 8;
            bool has_alpha = (header[8] & 8) != 0;
            if (24 == bpp && has_alpha)
                bpp = 32;
            return new MrlMetaData {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                BPP    = bpp,
                HasAlpha = has_alpha,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MrlMetaData)info;
            BitmapPalette palette = null;
            file.Position = 0x18;
            if (8 == info.BPP)
                palette = ReadPalette (file.AsStream);
            int stride = info.iWidth * (info.BPP / 8);
            int channel_size = info.iWidth * info.iHeight;
            var pixels = new byte[stride * info.iHeight];
            var input_length = (int)(file.Length - file.Position);
            var input = file.ReadBytes (input_length);

            DecryptInput (input, 8);
            MrlDecompress (input, pixels);
            RestoreOutput (pixels);

            byte[] image;
            if (8 == info.BPP)
            {
                if (!meta.HasAlpha)
                    return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, stride);
                stride = info.iWidth * 4;
                image = new byte[stride * info.iHeight];
                int src = 0;
                int asrc = channel_size;
                var colors = palette.Colors;
                for (int dst = 0; dst < image.Length; dst += 4)
                {
                    byte c = pixels[src++];
                    image[dst  ] = colors[c].B;
                    image[dst+1] = colors[c].G;
                    image[dst+2] = colors[c].R;
                    image[dst+3] = pixels[asrc++];
                }
            }
            else
            {
                image = new byte[pixels.Length];
                int channels = info.BPP / 8;
                int src = 0;
                for (int c = 0; c < channels; ++c)
                {
                    int dst = c;
                    for (int i = 0; i < channel_size; ++i)
                    {
                        image[dst] = pixels[src++];
                        dst += channels;
                    }
                }
            }
            PixelFormat format = meta.HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
            return ImageData.CreateFlipped (info, format, palette, image, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MrlFormat.Write not implemented");
        }

        internal static void DecryptInput (byte[] data, byte key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= key++;
            }
        }

        internal static void MrlDecompress (byte[] input, byte[] output)
        {
            int src = 0;
            int dst = 0;
            while (src < input.Length && dst < output.Length)
            {
                byte p = input[src++];
                if (p != 0)
                {
                    output[dst++] = p;
                }
                else
                {
                    int count = 1;
                    do
                    {
                        p = input[src++];
                        count += p;
                    }
                    while (0xFF == p);
                    dst += count;
                }
            }
        }

        internal static void RestoreOutput (byte[] data)
        {
            byte key = data[0];
            for (int i = 1; i < data.Length; ++i)
            {
                data[i] ^= key;
                key = data[i];
            }
        }
    }
}
