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
                               : meta.BPP == 32 ? PixelFormats.Bgra32 : PixelFormats.Bgr24;
            BitmapPalette palette = null;
            if (meta.BPP == 8)
            {
                file.Position = meta.PaletteOffset;
                palette = ReadPalette (file.AsStream);
            }
            int stride = meta.iWidth * meta.BPP / 8;
            file.Position = meta.DataOffset;
            var pixels = new byte[stride * meta.iHeight];
            if (!meta.IsCompressed)
            {
                file.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (meta, format, palette, pixels, stride);
            }
            if (!Decompress (file, pixels, meta.Depth + 2, meta.iWidth, meta.iHeight))
                throw new InvalidFormatException ("Invalid SWG file.");
            return ImageData.CreateFlipped (meta, format, palette, pixels, stride);
        }

        static readonly byte[] PlaneMap = { 2, 1, 0, 3 };

        bool Decompress (IBinaryStream input, byte[] output, int channels, int width, int height)
        {
            long start_pos = input.Position;
            byte hi = input.ReadUInt8();
            byte lo = input.ReadUInt8();
            if (hi != 0 || lo != 1)
            {
                input.Position = start_pos;
                int n = 0;
                for (int i = 0; i < channels; ++i)
                {
                    if (0 == input.ReadByte())
                        ++n;
                }
                if (n != channels)
                    return false;
                input.Position = start_pos + 4;
                hi = input.ReadUInt8();
                lo = input.ReadUInt8();
            }
            int compress_method = lo | hi << 8;
            if (0 == compress_method)
            {
                for (int i = 0; i < channels; ++i)
                {
                    int pos = i;
                    int count = height * width;
                    while (count --> 0)
                    {
                        output[pos] = input.ReadUInt8();
                        pos += channels;
                    }
                }
                return true;
            }
            if (compress_method != 1)
                return false;
            int stride = width * channels;
            var row_sizes = input.ReadBytes (2 * height * channels);
            int ctl_pos = 0;
            for (int c = 0; c < channels; ++c)
            for (int y = height - 1; y >= 0; --y)
            {
                int dst = stride * y + PlaneMap[c];
                int row_size = row_sizes[ctl_pos+1] | row_sizes[ctl_pos] << 8;
                ctl_pos += 2;
                DecompressRow (input, row_size, output, dst, channels);
            }
            return true;
        }

        internal static void DecompressRow (IBinaryStream input, int row_size, byte[] output, int dst, int step)
        {
            int x = 0;
            while (x < row_size)
            {
                byte ctl = input.ReadUInt8();
                if (ctl == 0)
                {
                    byte v = input.ReadUInt8();
                    x += 2;
                    output[dst] = v;
                    dst += step;
                }
                else if (ctl < 0x81u)
                {
                    int count = ctl + 1;
                    x += count + 1;
                    while (count --> 0)
                    {
                        output[dst] = input.ReadUInt8();
                        dst += step;
                    }
                }
                else
                {
                    byte v = input.ReadUInt8();
                    x += 2;
                    int count = 0x101 - ctl;
                    while (count --> 0)
                    {
                        output[dst] = v;
                        dst += step;
                    }
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SwgFormat.Write not implemented");
        }
    }
}
