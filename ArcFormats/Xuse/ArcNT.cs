//! \file       ArcNT.cs
//! \date       2023 Oct 17
//! \brief      Xuse resource archive.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class BgOpener : ArchiveFormat
    {
        public override string         Tag => "BG/Xuse";
        public override string Description => "Xuse bitmap archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public BgOpener ()
        {
            Extensions = new[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            if (!arc_name.StartsWith ("bg00", StringComparison.OrdinalIgnoreCase) &&
                !arc_name.StartsWith ("sbg", StringComparison.OrdinalIgnoreCase))
                return null;
            long rem;
            uint entry_size = arc_name[0] == 's' ? 0x96400u : 0x4B400u;
            int count = (int)Math.DivRem (file.MaxOffset, entry_size, out rem);
            if (rem != 0 || !IsSaneCount (count))
                return null;
            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", arc_name, i),
                    Type = "image",
                    Offset = offset,
                    Size = entry_size,
                };
                dir.Add (entry);
                offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            uint height = entry.Size == 0x4B400u ? 480u : 960u;
            var info = new ImageMetaData { Width = 640, Height = height, BPP = 8 };
            return new BgImageDecoder (input, info);
        }
    }

    internal class BgImageDecoder : BinaryImageDecoder
    {
        public BgImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0;
            var pixels = m_input.ReadBytes (Info.iWidth * Info.iHeight);
            var palette = ImageFormat.ReadPalette (m_input.AsStream);
            return ImageData.CreateFlipped (Info, PixelFormats.Indexed8, palette, pixels, Info.iWidth);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class HOpener : ArchiveFormat
    {
        public override string         Tag => "H/Xuse";
        public override string Description => "Xuse bitmap archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public HOpener ()
        {
            Extensions = new[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            var arc_name = Path.GetFileName (file.Name);
            if (!arc_name.StartsWith ("H", StringComparison.OrdinalIgnoreCase))
                return null;
            long rem;
            uint entry_size = arc_name.EndsWith ("W") ? 0x2A700u : 0x25480u;
            int count = (int)Math.DivRem (file.MaxOffset, entry_size, out rem);
            if (!IsSaneCount (count) || rem != 0)
                return null;
            uint offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", arc_name, i),
                    Type = "image",
                    Offset = offset,
                    Size = entry_size,
                };
                dir.Add (entry);
                offset += entry_size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            uint width = entry.Size == 0x25480u ? 280u : 320u;
            var info = new ImageMetaData { Width = width, Height = 480, BPP = 8 };
            return new HImageDecoder (input, info);
        }
    }

    internal class HImageDecoder : BinaryImageDecoder
    {
        public HImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0;
            int stride8bpp = Info.iWidth;
            int alpha_stride = (Info.iWidth / 8 + 1) & -2;
            var pixels = m_input.ReadBytes (stride8bpp * Info.iHeight);
            var alpha = m_input.ReadBytes (alpha_stride * Info.iHeight);
            m_input.Seek (-0x400, SeekOrigin.End);
            var palette = ImageFormat.ReadPalette (m_input.AsStream);
            int stride32bpp = Info.iWidth * 4;
            var bgra = new byte[stride32bpp * Info.iHeight];
            var colors = palette.Colors;
            int src = pixels.Length - stride8bpp;
            int asrc = alpha.Length - alpha_stride;
            int dst_row = 0;
            for (int y = 0; y < Info.iHeight; ++y)
            {
                int dst = dst_row;
                for (int x = 0; x < Info.iWidth; ++x)
                {
                    var c = colors[pixels[src+x]];
                    bgra[dst  ] = c.B;
                    bgra[dst+1] = c.G;
                    bgra[dst+2] = c.R;
                    int A = (alpha[asrc + (x >> 3)] << (x & 7)) & 0x80;
                    bgra[dst+3] = (byte)(A == 0 ? 0xFF : 0);
                    dst += 4;
                }
                src -= stride8bpp;
                asrc -= alpha_stride;
                dst_row += stride32bpp;
            }
            return ImageData.Create (Info, PixelFormats.Bgra32, null, bgra, stride32bpp);
        }
    }
}
