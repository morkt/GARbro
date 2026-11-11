//! \file       ImageAGI.cs
//! \date       2026-09-30
//! \brief      Artdink PS2 AGI texture format.
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
// A container of 20-byte texture descriptors followed by palette descriptors;
// pixel storage follows the PS2 GS pixel modes (psm byte at descriptor +6).

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Artdink
{
    internal class AgiMetaData : ImageMetaData
    {
        public uint TextureOffset;
        public uint PaletteOffset;
        public int  PixelMode;      // GS storage mode index: 1=24bpp, 2=16bpp, 3=8bpp, 4=4bpp
    }

    [Export(typeof(ImageFormat))]
    public class AgiFormat : ImageFormat
    {
        public override string         Tag { get { return "AGI"; } }
        public override string Description { get { return "Artdink PS2 AGI image format"; } }
        public override uint     Signature { get { return 0; } }

        public AgiFormat ()
        {
            Extensions = new string[] { "agi" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 8 + 20)
                return null;
            file.Position = 4;
            uint image_count = file.ReadUInt16 ();
            uint clut_count  = file.ReadUInt16 ();
            if (0 == image_count)
                return null;
            file.Position = 8;
            var entry = file.ReadBytes (20);
            if (entry.Length < 20)
                return null;
            uint texture_offset = LittleEndian.ToUInt32 (entry, 0);
            uint reg1           = LittleEndian.ToUInt32 (entry, 4);
            uint width          = LittleEndian.ToUInt16 (entry, 16);
            uint height         = LittleEndian.ToUInt16 (entry, 18);
            if (0 == width || 0 == height || width > 16384 || height > 16384)
                return null;
            int mode = (int)((reg1 >> 16) & 0xFF) & 7;
            bool has_clut = clut_count > 0;
            int pixel_mode;
            if (has_clut && 3 == mode)
                pixel_mode = 3;
            else if (has_clut && 4 == mode)
                pixel_mode = 4;
            else if (!has_clut && 2 == mode)
                pixel_mode = 2;
            else if (!has_clut && 1 == mode)
                pixel_mode = 1;
            else
                return null;
            int bpp;
            long needed;
            switch (pixel_mode)
            {
            case 1:  bpp = 24; needed = (long)width * height * 3; break;
            case 2:  bpp = 16; needed = (long)width * height * 2; break;
            case 3:  bpp = 8;  needed = (long)width * height;     break;
            default: bpp = 4;  needed = ((long)width + 1) / 2 * height; break;
            }
            if (texture_offset + needed > file.Length)
                return null;
            uint palette_offset = 0;
            if (has_clut)
            {
                long clut_entry = 8 + (long)image_count * 20;
                if (clut_entry + 20 > file.Length)
                    return null;
                file.Position = clut_entry;
                var clut = file.ReadBytes (20);
                if (clut.Length < 20)
                    return null;
                palette_offset = LittleEndian.ToUInt32 (clut, 0);
                long palette_size = 3 == pixel_mode ? 0x400 : 0x40;
                if (0 == palette_offset || palette_offset + palette_size > file.Length)
                    return null;
            }
            return new AgiMetaData {
                Width = width, Height = height, BPP = bpp,
                TextureOffset = texture_offset, PaletteOffset = palette_offset, PixelMode = pixel_mode,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (AgiMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            byte[] palette = null;
            if (3 == meta.PixelMode)
            {
                file.Position = meta.PaletteOffset;
                var raw_pal = file.ReadBytes (0x400);
                if (raw_pal.Length < 0x400)
                    throw new InvalidFormatException ();
                palette = Ps2ImageUtil.BuildPs2Palette256 (raw_pal);
            }
            else if (4 == meta.PixelMode)
            {
                file.Position = meta.PaletteOffset;
                var raw_pal = file.ReadBytes (0x40);
                if (raw_pal.Length < 0x40)
                    throw new InvalidFormatException ();
                palette = Ps2ImageUtil.BuildPalette (raw_pal, 16, true);
            }
            int row_size;
            switch (meta.PixelMode)
            {
            case 1:  row_size = width * 3; break;
            case 2:  row_size = width * 2; break;
            case 3:  row_size = width;     break;
            default: row_size = (width + 1) / 2; break;
            }
            file.Position = meta.TextureOffset;
            var src = file.ReadBytes (row_size * height);
            if (src.Length < row_size * height)
                throw new InvalidFormatException ();
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; ++y)
            {
                int s = y * row_size;
                int d = y * width * 4;
                switch (meta.PixelMode)
                {
                case 1:  Ps2ImageUtil.ConvertRow24 (src, s, pixels, d, width); break;
                case 2:  Ps2ImageUtil.ConvertRow555 (src, s, pixels, d, width); break;
                case 3:  Ps2ImageUtil.ConvertRow8 (src, s, pixels, d, width, palette); break;
                default: Ps2ImageUtil.ConvertRow4 (src, s, pixels, d, width, palette); break;
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("AgiFormat.Write not implemented");
        }
    }
}