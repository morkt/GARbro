//! \file       ImagePB6.cs
//! \date       2026-09-30
//! \brief      ACTGS engine PB6 image format.
//
// Copyright (C) 2026 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.Actgs
{
    internal class Pb6MetaData : ImageMetaData
    {
        public int DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class Pb6Format : ImageFormat
    {
        public override string         Tag { get { return "PB6"; } }
        public override string Description { get { return "ACTGS engine PS2 image format"; } }
        public override uint     Signature { get { return 0; } }

        public Pb6Format ()
        {
            Extensions = new string[] { "bmp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 54)
                return null;
            file.Position = 0;
            var magic = file.ReadBytes (3);
            if (magic.Length < 3 || magic[0] != 'P' || magic[1] != 'B' || magic[2] != '6')
                return null;
            file.Position = 18;
            int width  = file.ReadUInt16 ();
            file.Position = 22;
            int height = file.ReadUInt16 ();
            file.Position = 28;
            int bpp    = file.ReadUInt16 ();
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
                return null;
            if (8 != bpp && 24 != bpp && 32 != bpp)
                return null;
            int data_offset = 8 == bpp ? 54 + 0x400 : 54;
            if (data_offset > file.Length)
                return null;
            return new Pb6MetaData {
                Width = (uint)width, Height = (uint)height, BPP = bpp,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Pb6MetaData)info;
            int pixel_size = meta.BPP / 8;
            int decoded_size = (int)meta.Width * (int)meta.Height * pixel_size;
            byte[] pixels;
            file.Position = meta.DataOffset;
            if (8 == meta.BPP)
            {
                if (meta.DataOffset + decoded_size > file.Length)
                    throw new EndOfStreamException ();
                pixels = file.ReadBytes (decoded_size);
            }
            else
            {
                pixels = DecodeRle (file, decoded_size, pixel_size);
            }
            // 8bpp indices are expanded to Bgra32: a palette entry with all-zero
            // RGB components marks a transparent pixel.  The game reads palette
            // entries as B,G,R (it packs the first byte into the high color bits,
            // while the 24/32bpp pixel data is ordered R first); the file palette
            // itself is linear, the game only rearranges it when uploading the
            // CLUT to the GS.
            BitmapPalette palette = null;
            if (8 == meta.BPP)
            {
                file.Position = 54;
                palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.BgrX);
            }
            // pixel rows are stored top-down within the file, but represent a
            // bottom-up bitmap; flip when filling the output.
            var output = new byte[(int)meta.Width*(int)meta.Height*4];
            for (int src_y = 0; src_y < meta.Height; ++src_y)
            {
                int dst_y = (int)meta.Height-1-src_y;
                int src_row = src_y*(int)meta.Width*pixel_size;
                int dst_row = dst_y*(int)meta.Width*4;
                for (int x = 0; x < meta.Width; ++x)
                {
                    int s = src_row + x*pixel_size;
                    int d = dst_row + x*4;
                    if (8 == meta.BPP)
                    {
                        var color = palette.Colors[pixels[s]];
                        output[d  ] = color.B;
                        output[d+1] = color.G;
                        output[d+2] = color.R;
                        output[d+3] = 0 == (color.R | color.G | color.B) ? (byte)0 : (byte)0xFF;
                    }
                    else if (24 == meta.BPP)
                    {
                        output[d  ] = pixels[s+2]; // B
                        output[d+1] = pixels[s+1]; // G
                        output[d+2] = pixels[s];   // R
                        output[d+3] = 0xFF;
                    }
                    else
                    {
                        output[d  ] = pixels[s+3]; // B
                        output[d+1] = pixels[s+2]; // G
                        output[d+2] = pixels[s+1]; // R
                        output[d+3] = pixels[s];   // A
                    }
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, output);
        }

        // RLE: control byte = run length (bit 7 set = fill with the pixel that
        // follows, bit 7 clear = literal run), decoded_size bounds the output.
        // Truncated files stop early; the tail stays black.
        byte[] DecodeRle (IBinaryStream input, int decoded_size, int pixel_size)
        {
            var output = new byte[decoded_size];
            var pixel = new byte[pixel_size];
            int dst = 0;
            while (dst < output.Length)
            {
                int control = input.ReadByte ();
                if (control < 0)
                    break;
                int count = control & 0x7F;
                if (0 == count)
                    throw new InvalidFormatException ();
                int take = Math.Min (count, (output.Length - dst) / pixel_size);
                if (0 != (control & 0x80))
                {
                    int read = input.Read (pixel, 0, pixel_size);
                    if (read < pixel_size)
                        break;
                    for (int i = 0; i < take; ++i)
                    {
                        Buffer.BlockCopy (pixel, 0, output, dst, pixel_size);
                        dst += pixel_size;
                    }
                }
                else
                {
                    int bytes = take * pixel_size;
                    int read = input.Read (output, dst, bytes);
                    dst += read;
                    if (read < bytes)
                        break;
                }
            }
            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Pb6Format.Write not implemented");
        }
    }
}
