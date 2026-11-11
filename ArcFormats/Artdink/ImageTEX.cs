//! \file       ImageTEX.cs
//! \date       2026-09-30
//! \brief      Artdink PS2 TEX image format.
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
// A sprite table of 20-byte descriptors; sprites use PS2 GS pixel modes and
// are composed vertically into a single bitmap, with an optional CLUT.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Artdink
{
    internal struct TexSprite
    {
        public uint RelOffset;
        public byte Psm;
        public int  Width;
        public int  Height;
    }

    internal class TexMetaData : ImageMetaData
    {
        public uint BaseOffset;
        public bool HasClut;
        public List<TexSprite> Sprites;
    }

    [Export(typeof(ImageFormat))]
    public class TexFormat : ImageFormat
    {
        public override string         Tag { get { return "TEX"; } }
        public override string Description { get { return "Artdink PS2 TEX image format"; } }
        public override uint     Signature { get { return 0x20584554; } } // 'TEX '

        public TexFormat ()
        {
            Extensions = new string[] { "tex" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 0x28 + 20)
                return null;
            file.Position = 0x14;
            uint width  = file.ReadUInt32 ();
            uint height = file.ReadUInt32 ();
            file.Position = 0x20;
            uint base_offset  = file.ReadUInt32 ();
            uint sprite_count = file.ReadUInt16 ();
            uint has_clut     = file.ReadUInt16 ();
            if (0 == width || 0 == height || width > 16384 || height > 16384)
                return null;
            if (0 == sprite_count || sprite_count > 0x1000)
                return null;
            long sprites_end = 0x28 + (long)sprite_count * 20;
            if (sprites_end > file.Length)
                return null;
            if (has_clut != 0 && sprites_end + 4 > file.Length)
                return null;
            var sprites = new List<TexSprite> ((int)sprite_count);
            int max_bpp = 0;
            file.Position = 0x28;
            for (uint i = 0; i < sprite_count; ++i)
            {
                var entry = file.ReadBytes (20);
                if (entry.Length < 20)
                    return null;
                var sprite = new TexSprite {
                    RelOffset = LittleEndian.ToUInt32 (entry, 0),
                    Psm       = entry[6],
                    Width     = LittleEndian.ToUInt16 (entry, 16),
                    Height    = LittleEndian.ToUInt16 (entry, 18),
                };
                sprites.Add (sprite);
                max_bpp = Math.Max (max_bpp, PsmToBpp (sprite.Psm));
            }
            return new TexMetaData {
                Width = width, Height = height, BPP = max_bpp > 0 ? max_bpp : 8,
                BaseOffset = base_offset, HasClut = has_clut != 0, Sprites = sprites,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TexMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            byte[] pal8 = null;
            byte[] pal4 = null;
            if (meta.HasClut)
            {
                long clut_addr = 0x28 + (long)meta.Sprites.Count * 20;
                file.Position = clut_addr;
                uint clut_rel = file.ReadUInt32 ();
                long clut_offset = (long)meta.BaseOffset + clut_rel;
                if (clut_offset + 0x400 <= file.Length)
                {
                    file.Position = clut_offset;
                    var raw_pal = file.ReadBytes (0x400);
                    if (raw_pal.Length == 0x400)
                    {
                        pal8 = Ps2ImageUtil.BuildPs2Palette256 (raw_pal);
                        pal4 = Ps2ImageUtil.BuildPalette (raw_pal, 16, true);
                    }
                }
            }
            var pixels = new byte[width * height * 4];
            long current_y = 0;
            foreach (var sprite in meta.Sprites)
            {
                if (0 == sprite.Width || 0 == sprite.Height)
                    continue;
                int bpp = PsmToBpp (sprite.Psm);
                if (0 == bpp)
                    continue;
                int row_size = (sprite.Width * bpp + 7) / 8;
                long src_size = (long)row_size * sprite.Height;
                long src_offset = (long)meta.BaseOffset + sprite.RelOffset;
                if (src_offset + src_size > file.Length)
                    throw new InvalidFormatException ();
                file.Position = src_offset;
                var src = file.ReadBytes ((int)src_size);
                int copy_width = Math.Min (sprite.Width, width);
                for (int y = 0; y < sprite.Height; ++y)
                {
                    long dest_y = current_y + y;
                    if (dest_y >= height)
                        break;
                    int s = y * row_size;
                    int d = (int)(dest_y * width * 4);
                    if (0x00 == sprite.Psm)
                        Ps2ImageUtil.ConvertRow32 (src, s, pixels, d, copy_width);
                    else if (0x01 == sprite.Psm)
                        Ps2ImageUtil.ConvertRow24 (src, s, pixels, d, copy_width);
                    else if (0x02 == sprite.Psm)
                        Ps2ImageUtil.ConvertRow555 (src, s, pixels, d, copy_width);
                    else if (0x13 == sprite.Psm && null != pal8)
                        Ps2ImageUtil.ConvertRow8 (src, s, pixels, d, copy_width, pal8);
                    else if (0x14 == sprite.Psm && null != pal4)
                        Ps2ImageUtil.ConvertRow4 (src, s, pixels, d, copy_width, pal4);
                }
                current_y += sprite.Height;
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        static int PsmToBpp (byte psm)
        {
            switch (psm)
            {
            case 0x00: return 32;
            case 0x01: return 24;
            case 0x02: return 16;
            case 0x13: return 8;
            case 0x14: return 4;
            }
            return 0;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TexFormat.Write not implemented");
        }
    }
}