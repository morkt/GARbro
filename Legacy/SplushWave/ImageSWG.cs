//! \file       ImageSWG.cs
//! \date       2023 Aug 14
//! \brief      Splush Wave graphics format.
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

namespace GameRes.Formats.SplushWave
{
    internal class SwgMetaData : ImageMetaData
    {
        public uint PaletteOffset;
        public uint DataOffset;
        public byte Depth;
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class SwgFormat : ImageFormat
    {
        public override string         Tag { get { return "SWG"; } }
        public override string Description { get { return "Splush Wave Graphics format"; } }
        public override uint     Signature { get { return 0x475753; } } // 'SWG'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            uint pal_offset = header.ToUInt32 (0x14);
            if (pal_offset != 0)
                pal_offset += 0x10;
            byte depth = header[0x28];
            return new SwgMetaData {
                Width  = header.ToUInt16 (0x20),
                Height = header.ToUInt16 (0x22),
                BPP    = pal_offset != 0 ? 8 : depth == 2 ? 32 : 24,
                DataOffset = header.ToUInt32 (0x10) + 0x10,
                PaletteOffset = pal_offset,
                Depth = depth,
                IsCompressed = header[0x2F] != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (SwgMetaData)info;
            PixelFormat format = meta.BPP == 8  ? PixelFormats.Indexed8
                               : meta.BPP == 32 ? PixelFormats.Bgr32 : PixelFormats.Bgr24;
            BitmapPalette palette = null;
            if (meta.BPP == 8)
            {
                file.Position = meta.PaletteOffset;
                palette = ReadPalette (file.AsStream);
            }
            int stride = meta.iWidth * meta.BPP / 8;
            file.Position = meta.DataOffset;
//            var pixels = new byte[stride * meta.iHeight];
            var pixels = new byte[4 * meta.iWidth * meta.iHeight];
            if (!meta.IsCompressed)
            {
                file.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (meta, format, palette, pixels, stride);
            }
            var input = file.ReadBytes ((int)(file.Length - file.Position));
            if (!Decompress (input, pixels, meta.Depth + 2, meta.iWidth, meta.iHeight))
                throw new InvalidFormatException ("Invalid SWG file.");
            return ImageData.CreateFlipped (meta, format, palette, pixels, stride);
        }

        bool Decompress (byte[] input, byte[] output, int channels, int width, int height)
        {
            int src = 0;
            if (input[0] != 0 || input[1] != 1)
            {
                int n = 0;
                for (int i = 0; i < channels; ++i)
                {
                    if (0 == input[i])
                        ++n;
                }
                if (n != channels)
                    return false;
                src = 4;
            }
            int compress_method = input[src+1] + (input[src] << 8);
            src += 2;
            if (0 == compress_method)
            {
                for (int i = 0; i < channels; ++i)
                {
                    int pos = i;
                    int count = height * width;
                    while (count --> 0)
                    {
                        output[pos] = input[src++];
                        pos += channels;
                    }
                }
                return true;
            }
            if (compress_method != 1)
                return false;
            int dst = 0;
            int v33 = src;
            int v37 = height * channels;
            src += 2 * v37;
            for (int row = 0; row < v37; ++row)
            {
                int y = row % height;
                dst = channels * (width * (height - y - 1) + 1) - row / height - 1;
                if (dst > output.Length)
                    return true;
                int v24 = 0;
                int v36 = input[v33+1] + (input[v33] << 8);
                v33 += 2;
                do
                {
                    byte lo = input[src];
                    byte hi = input[src+1];
                    if (lo != 0)
                    {
                        if (lo < 0x81)
                        {
                            ++src;
                            int count = lo + 1;
                            v24 += count + 1;
                            while (count --> 0)
                            {
                                output[dst] = input[src++];
                                dst += channels;
                            }
                        }
                        else
                        {
                            src += 2;
                            v24 += 2;
                            int count = Math.Min (0x101 - lo, output.Length - dst);
                            while (count --> 0)
                            {
                                output[dst] = hi;
                                dst += channels;
                            }
                        }
                    }
                    else
                    {
                        src += 2;
                        v24 += 2;
                        output[dst] = hi;
                        dst += channels;
                    }
                    if (dst >= output.Length)
                        return true;
                }
                while (v24 < v36);
            }
            return true;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SwgFormat.Write not implemented");
        }
    }
}
