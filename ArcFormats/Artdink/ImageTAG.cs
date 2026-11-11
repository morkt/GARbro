//! \file       ImageTAG.cs
//! \date       2026-09-30
//! \brief      Artdink PS2 TAG image format.
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
// PS2 GIF transfer packets describe a stack of 4/8bpp images; the decoded
// images are composed vertically into a single bitmap.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Artdink
{
    internal struct TransferEvent
    {
        public int Offset;
        public int Size;
        public int Width;
        public int Height;
    }

    internal struct TagImage
    {
        public int PaletteOffset;
        public int ColorCount;
        public int Width;
        public int Height;
        public int PixelOffset;
        public int PixelSize;
    }

    internal class TagMetaData : ImageMetaData
    {
        public List<TagImage> Images;
    }

    [Export(typeof(ImageFormat))]
    public class TagFormat : ImageFormat
    {
        public override string         Tag { get { return "TAG"; } }
        public override string Description { get { return "Artdink PS2 TAG image format"; } }
        public override uint     Signature { get { return 0; } }

        public TagFormat ()
        {
            Extensions = new string[] { "tag" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 0x40 || file.Length > int.MaxValue)
                return null;
            file.Position = 0;
            var data = file.ReadBytes ((int)file.Length);
            if (data.Length < 0x40)
                return null;
            var images = ParseImages (data);
            if (0 == images.Count)
                return null;
            long width = 0;
            long height = 0;
            int bpp = 0;
            foreach (var image in images)
            {
                if (image.Width <= 0 || image.Height <= 0 || image.Width > 16384 || image.Height > 16384)
                    return null;
                if (image.Width > width)
                    width = image.Width;
                height += image.Height;
                bpp = Math.Max (bpp, 256 == image.ColorCount ? 8 : 4);
            }
            if (0 == width || height > 65535 || width*height*4 > int.MaxValue)
                return null;
            return new TagMetaData {
                Width = (uint)width, Height = (uint)height, BPP = bpp, Images = images,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TagMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            file.Position = 0;
            var data = file.ReadBytes ((int)file.Length);
            var pixels = new byte[width * height * 4];
            int current_y = 0;
            foreach (var image in meta.Images)
            {
                var raw_pal = new byte[image.ColorCount * 4];
                Buffer.BlockCopy (data, image.PaletteOffset, raw_pal, 0, raw_pal.Length);
                var palette = 256 == image.ColorCount
                    ? Ps2ImageUtil.BuildPs2Palette256 (raw_pal)
                    : Ps2ImageUtil.BuildPalette (raw_pal, 16, true);
                int bpp = 16 == image.ColorCount ? 4 : 8;
                int row_size = (image.Width * bpp + 7) / 8;
                long valid = Math.Min (image.PixelSize, (long)image.Width * image.Height * bpp / 8);
                valid = Math.Min (valid, data.Length - image.PixelOffset);
                var row = new byte[row_size];
                for (int y = 0; y < image.Height; ++y)
                {
                    long row_start = (long)y * row_size;
                    if (row_start >= valid)
                        break;
                    int copy = (int)Math.Min (row_size, valid - row_start);
                    Array.Clear (row, 0, row.Length);
                    Buffer.BlockCopy (data, (int)(image.PixelOffset + row_start), row, 0, copy);
                    int dest_y = current_y + y;
                    if (dest_y >= height)
                        break;
                    int d = dest_y * width * 4;
                    if (8 == bpp)
                        Ps2ImageUtil.ConvertRow8 (row, 0, pixels, d, image.Width, palette);
                    else
                        Ps2ImageUtil.ConvertRow4 (row, 0, pixels, d, image.Width, palette);
                }
                current_y += image.Height;
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        static List<TagImage> ParseImages (byte[] data)
        {
            var events = new List<TransferEvent> ();
            var seen = new HashSet<int> ();
            int packet_width = 0;
            int packet_height = 0;

            void Walk (int cursor)
            {
                while (cursor >= 0 && cursor + 16 <= data.Length && seen.Add (cursor))
                {
                    uint w0 = LittleEndian.ToUInt32 (data, cursor);
                    int addr = (int)LittleEndian.ToUInt32 (data, cursor + 4);
                    switch (w0 & 0x70000000)
                    {
                    case 0x10000000:
                        if ((w0 & 0xFFFF) > 0)
                            ReadRecords (data, cursor + 16, ref packet_width, ref packet_height);
                        cursor += 16 + (int)(w0 & 0xFFFF) * 16;
                        break;
                    case 0x20000000:
                        cursor = addr;
                        break;
                    case 0x30000000:
                    case 0x40000000:
                        events.Add (new TransferEvent {
                            Offset = addr,
                            Size = (int)(w0 & 0xFFFF) * 16,
                            Width = packet_width,
                            Height = packet_height,
                        });
                        packet_width = 0;
                        packet_height = 0;
                        cursor += 16;
                        break;
                    case 0x50000000:
                        Walk (addr);
                        cursor += 16 + (int)(w0 & 0xFFFF) * 16;
                        break;
                    default:
                        return;
                    }
                }
            }

            Walk ((int)LittleEndian.ToUInt32 (data, 4));

            var images = new List<TagImage> ();
            for (int i = 0; i + 1 < events.Count; i += 2)
            {
                var pal = events[i];
                var pix = events[i+1];
                if (pal.Width <= 0 || pal.Height <= 0 || pix.Width <= 0 || pix.Height <= 0)
                    return new List<TagImage> ();
                long colors = (long)pal.Width * pal.Height;
                if (16 != colors && 256 != colors)
                    return new List<TagImage> ();
                int bpp = 16 == colors ? 4 : 8;
                long needed = (long)pix.Width * pix.Height * bpp / 8;
                if (pal.Size != colors * 4 || Math.Abs (pix.Size - needed) > 16)
                    return new List<TagImage> ();
                if (pal.Offset < 0 || pal.Offset + colors * 4 > data.Length)
                    return new List<TagImage> ();
                if (pix.Offset < 0 || pix.Offset > data.Length)
                    return new List<TagImage> ();
                images.Add (new TagImage {
                    PaletteOffset = pal.Offset,
                    ColorCount = (int)colors,
                    Width = pix.Width,
                    Height = pix.Height,
                    PixelOffset = pix.Offset,
                    PixelSize = pix.Size,
                });
            }
            return images;
        }

        static void ReadRecords (byte[] data, int packet, ref int width, ref int height)
        {
            if (packet + 4 > data.Length)
                return;
            int count = (int)(LittleEndian.ToUInt32 (data, packet) & 0xFFFF);
            for (int i = 0; i < count; ++i)
            {
                int record = packet + 16 + i * 16;
                if (record + 16 > data.Length)
                    return;
                if (0x52 == LittleEndian.ToUInt32 (data, record + 12))
                {
                    width  = LittleEndian.ToInt32 (data, record + 4);
                    height = LittleEndian.ToInt32 (data, record + 8);
                }
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TagFormat.Write not implemented");
        }
    }
}
