//! \file       ImageBIP.cs
//! \date       Thu Mar 05 09:36:40 2015
//! \brief      BIP tiled bitmap format.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    internal class BipMetaData : ImageMetaData
    {
        public readonly List<BipTile> Tiles = new List<BipTile>();
    }

    internal class BipTile
    {
        public int  Left;
        public int  Top;
        public int  Width;
        public int  Height;
        public uint Offset;
    }

    [Export(typeof(ImageFormat))]
    public class BipFormat : ImageFormat
    {
        public override string         Tag { get { return "BIP"; } }
        public override string Description { get { return "PS2 tiled bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("BipFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new BinaryReader (stream, Encoding.ASCII, true))
            {
                int sig = input.ReadInt32();
                if (sig != 5 && sig != 10)
                    return null;
                uint header_end = (uint)sig*4;
                uint index_offset = input.ReadUInt32();

                input.BaseStream.Position = header_end-4;
                uint data_offset = input.ReadUInt32() + 8;
                if (index_offset >= data_offset || index_offset < header_end)
                    return null;
                input.BaseStream.Position = index_offset;
                int tile_count = input.ReadInt16();
                int flag = input.ReadInt16();
                if (tile_count <= 0 || 0 != flag)
                    return null;
                input.ReadInt32();
                uint w = input.ReadUInt16();
                uint h = input.ReadUInt16();
                if (0 == w || 0 == h)
                    return null;
                var meta = new BipMetaData { Width = w, Height = h, BPP = 32 };
                meta.Tiles.Capacity = tile_count;
                for (int i = 0; i < tile_count; ++i)
                {
                    input.ReadInt64();
                    var tile = new BipTile();
                    tile.Left   = input.ReadUInt16();
                    tile.Top    = input.ReadUInt16();
                    tile.Width  = input.ReadUInt16();
                    tile.Height = input.ReadUInt16();
                    if (tile.Left + tile.Width > meta.Width)
                        meta.Width = (uint)(tile.Left + tile.Width);
                    if (tile.Top  + tile.Height > meta.Height)
                        meta.Height = (uint)(tile.Top + tile.Height);
                    input.ReadInt64();
                    tile.Offset = input.ReadUInt32() + data_offset;
                    meta.Tiles.Add (tile);
                }
                return meta;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as BipMetaData;
            if (null == meta)
                throw new ArgumentException ("BipFormat.Read should be supplied with BipMetaData", "info");

            var header = new byte[0x7c];
            var bitmap = new WriteableBitmap ((int)meta.Width, (int)meta.Height,
                    ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            foreach (var tile in meta.Tiles)
            {
                stream.Position = tile.Offset;
                if (header.Length != stream.Read (header, 0, header.Length))
                    throw new InvalidFormatException ("Invalid tile header");
                if (!Binary.AsciiEqual (header, "PNGFILE2"))
                    throw new InvalidFormatException ("Unknown tile format");
                int data_size = LittleEndian.ToInt32 (header, 0x18) - header.Length;
                int alpha = LittleEndian.ToInt32 (header, 0x68);
                int x = LittleEndian.ToInt32 (header, 0x6c);
                int y = LittleEndian.ToInt32 (header, 0x70);
                using (var png = new StreamRegion (stream, stream.Position, data_size, true))
                {
                    var decoder = new PngBitmapDecoder (png,
                        BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    BitmapSource frame = decoder.Frames[0];
                    PixelFormat format = 0 == alpha ? PixelFormats.Bgr32 : PixelFormats.Bgra32;
                    var converted = new FormatConvertedBitmap (frame, format, null, 0);
                    int stride = converted.PixelWidth * 4;
                    var pixels = new byte[stride * converted.PixelHeight];
                    converted.CopyPixels (pixels, stride, 0);
                    for (int p = 0; p < pixels.Length; p += 4)
                    {
                        byte r = pixels[p];
                        pixels[p] = pixels[p+2];
                        pixels[p+2] = r;
                        int a = 0 == alpha ? 0xff : pixels[p+3] * 0xff / 0x80;
                        if (a > 0xff) a = 0xff;
                        pixels[p+3] = (byte)a;
                    }
                    var rect = new Int32Rect (tile.Left+x, tile.Top+y, converted.PixelWidth, converted.PixelHeight);
                    bitmap.WritePixels (rect, pixels, stride, 0);
                }
            }
            bitmap.Freeze();
            return new ImageData (bitmap, meta);
        }
    }
}
