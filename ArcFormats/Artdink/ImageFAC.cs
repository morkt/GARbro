//! \file       ImageFAC.cs
//! \date       2026-09-30
//! \brief      Artdink PS2 FAC image format.
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
// A fixed 16-byte pattern marks the header end; 8bpp pixels follow, with a
// 256-color PS2 CLUT right after them.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Artdink
{
    internal class FacMetaData : ImageMetaData
    {
        public long PixelOffset;
        public long PaletteOffset;
    }

    [Export(typeof(ImageFormat))]
    public class FacFormat : ImageFormat
    {
        public override string         Tag { get { return "FAC"; } }
        public override string Description { get { return "Artdink PS2 FAC image format"; } }
        public override uint     Signature { get { return 0; } }

        static readonly byte[] FacPattern = {
            0x00,0x00,0x00,0x00, 0x01,0x00,0x00,0x00, 0x00,0x00,0x00,0x00, 0x10,0x00,0x10,0x00,
        };

        public FacFormat ()
        {
            Extensions = new string[] { "fac" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 0x40 + 16)
                return null;
            long pattern_offset = FindPattern (file);
            if (pattern_offset < 0)
                return null;
            long header_offset = pattern_offset - 0x40;
            if (header_offset < 0)
                return null;
            file.Position = header_offset + 0x38;
            uint width  = file.ReadUInt16 ();
            uint height = file.ReadUInt16 ();
            if (0 == width || 0 == height || width > 16384 || height > 16384)
                return null;
            long pixel_offset = pattern_offset + 16;
            long pixel_count = (long)width * height;
            long palette_offset = pixel_offset + pixel_count;
            if (pixel_offset + pixel_count > file.Length || palette_offset + 0x400 > file.Length)
                return null;
            return new FacMetaData {
                Width = width, Height = height, BPP = 8,
                PixelOffset = pixel_offset, PaletteOffset = palette_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (FacMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            file.Position = meta.PaletteOffset;
            var raw_pal = file.ReadBytes (0x400);
            if (raw_pal.Length < 0x400)
                throw new InvalidFormatException ();
            var palette = Ps2ImageUtil.BuildPs2Palette256 (raw_pal);
            file.Position = meta.PixelOffset;
            var src = file.ReadBytes (width * height);
            if (src.Length < width * height)
                throw new InvalidFormatException ();
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; ++y)
                Ps2ImageUtil.ConvertRow8 (src, y * width, pixels, y * width * 4, width, palette);
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        // The pattern may occur anywhere in the file, so scan it in chunks.
        static long FindPattern (IBinaryStream file)
        {
            const int chunk_size = 0x10000;
            int pattern_size = FacPattern.Length;
            var buffer = new byte[chunk_size + pattern_size - 1];
            long base_offset = 0;
            int keep = 0;
            file.Position = 0;
            while (base_offset + keep < file.Length)
            {
                int read = file.Read (buffer, keep, chunk_size);
                if (read <= 0)
                    break;
                int total = keep + read;
                int limit = total - pattern_size + 1;
                for (int i = 0; i < limit; ++i)
                {
                    int j = 0;
                    while (j < pattern_size && buffer[i+j] == FacPattern[j])
                        ++j;
                    if (j == pattern_size)
                        return base_offset + i;
                }
                keep = Math.Min (pattern_size - 1, total);
                Buffer.BlockCopy (buffer, total - keep, buffer, 0, keep);
                base_offset += total - keep;
            }
            return -1;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("FacFormat.Write not implemented");
        }
    }
}