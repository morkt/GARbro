//! \file       ImageTXF.cs
//! \date       2026-09-30
//! \brief      Artdink PS2 TXF image format.
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
// A glyph descriptor is located through the offset stored at 0x30; 8bpp indexed
// pixels use a 256-color PS2 CLUT occupying the last 0x400 bytes of the file.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Artdink
{
    internal class TxfMetaData : ImageMetaData
    {
        public long PixelOffset;
        public long PaletteOffset;
    }

    [Export(typeof(ImageFormat))]
    public class TxfFormat : ImageFormat
    {
        public override string         Tag { get { return "TXF"; } }
        public override string Description { get { return "Artdink PS2 TXF image format"; } }
        public override uint     Signature { get { return 0; } }

        public TxfFormat ()
        {
            Extensions = new string[] { "txf" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 0x40)
                return null;
            file.Position = 0x30;
            int glyph_offset = file.ReadInt32 ();
            if (glyph_offset < 0 || glyph_offset + 0x20 > file.Length)
                return null;
            file.Position = glyph_offset + 4;
            uint count = file.ReadUInt16 ();
            if (0 == count)
                return null;
            file.Position = glyph_offset + 8;
            int pixel_rel = file.ReadInt32 ();
            long pixel_offset = glyph_offset + pixel_rel;
            if (pixel_offset < 0 || pixel_offset >= file.Length)
                return null;
            file.Position = glyph_offset + 0x0E;
            byte pixel_format = (byte)file.ReadByte ();
            if (0x13 != pixel_format && 0x1B != pixel_format)
                return null;
            file.Position = glyph_offset + 0x18;
            uint width  = file.ReadUInt16 ();
            uint height = file.ReadUInt16 ();
            if (0 == width || 0 == height || width > 4096 || height > 4096)
                return null;
            long pixel_count = (long)width * height;
            const int palette_size = 0x400;
            if (file.Length < palette_size + pixel_offset)
                return null;
            long palette_offset = file.Length - palette_size;
            if (palette_offset - pixel_offset < pixel_count)
                return null;
            return new TxfMetaData {
                Width = width, Height = height, BPP = 8,
                PixelOffset = pixel_offset, PaletteOffset = palette_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TxfMetaData)info;
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

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TxfFormat.Write not implemented");
        }
    }
}