//! \file       ArcCG.cs
//! \date       Tue Feb 16 02:48:55 2016
//! \brief      Software House Parsley CG archive.
//
// Copyright (C) 2016-2017 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class CgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/yanepack"; } }
        public override string Description { get { return "YaneSDK resouce archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CgOpener ()
        {
            Extensions = new string[] { "", "dat" };
            Signatures = new uint[] { 0, 0x656E6179 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.AsciiEqual (0, "yane"))
            {
                if (!file.View.AsciiEqual (4, "pack"))
                    return null;
            }
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            int first_offset = file.View.ReadInt32 (0x2C);
            if (12 + count*0x28 != first_offset)
                return null;

            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x24);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class CgV1Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CG/PARSLEY/1"; } }
        public override string Description { get { return "Software House Parsley CG archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CgV1Opener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var arc_name = Path.GetFileName (file.Name);
            bool is_ucg = arc_name.StartsWith ("UCG", StringComparison.OrdinalIgnoreCase);
            bool is_cg = is_ucg || arc_name == "CG";

            using (var index = file.CreateStream())
            {
                index.Position = 4;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString();
                    if (string.IsNullOrWhiteSpace (name) || name.Length > 0x100)
                        return null;
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = index.ReadUInt32();
                    if (entry.Offset >= file.MaxOffset)
                        return null;
                    entry.IsPacked = is_ucg;
                    dir.Add (entry);
                }
                long index_end = index.Position;
                for (int i = 0; i < count; ++i)
                {
                    var entry = dir[i];
                    if (entry.Offset < index_end)
                        return null;
                    long next_offset = i+1 < count ? dir[i+1].Offset : file.MaxOffset;
                    entry.Size = (uint)(next_offset - entry.Offset);
                    if (is_cg)
                        entry.Type = "image";
                }
                if (is_cg && !is_ucg)
                {
                    var palette_entry = dir.Find (e => e.Name.Equals ("Palette", StringComparison.OrdinalIgnoreCase));
                    if (palette_entry != null && 1 == file.View.ReadByte (palette_entry.Offset+8))
                    {
                        var palette = ImageFormat.ReadPalette (file, palette_entry.Offset+9, 0x100, PaletteFormat.Rgb);
                        return new CgArchive (file, this, dir, palette);
                    }
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            int type = arc.File.View.ReadByte (entry.Offset+8);
            if (type > 1)
                return base.OpenImage (arc, entry);
            var pent = entry as PackedEntry;
            bool packed = pent != null && pent.IsPacked;
            var info = new CgMetaData {
                Width = arc.File.View.ReadUInt32 (entry.Offset),
                Height = arc.File.View.ReadUInt32 (entry.Offset+4),
                BPP = packed ? 16 : 8,
                HasPalette = type == 1,
            };
            var cg_arc = arc as CgArchive;
            if (cg_arc != null)
                info.DefaultPalette = cg_arc.DefaultPalette;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (packed)
                return new UCgDecoder (input, info);
            else
                return new CgDecoder (input, info);
        }
    }

    internal class CgMetaData : ImageMetaData
    {
        public bool             HasPalette;
        public BitmapPalette    DefaultPalette;
    }

    internal class CgArchive : ArcFile
    {
        public readonly BitmapPalette DefaultPalette;

        public CgArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, BitmapPalette palette)
            : base (arc, impl, dir)
        {
            DefaultPalette = palette;
        }
    }

    internal class CgDecoder : BinaryImageDecoder
    {
        bool            m_has_palette;
        BitmapPalette   m_palette;

        public CgDecoder (IBinaryStream input, CgMetaData info) : base (input, info)
        {
            m_has_palette = info.HasPalette;
            m_palette = info.DefaultPalette;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 9;
            if (m_has_palette)
                ReadPalette();
            var pixels = m_input.ReadBytes ((int)(Info.Width * Info.Height));
            if (m_palette != null)
                return ImageData.Create (Info, PixelFormats.Indexed8, m_palette, pixels);
            else
                return ImageData.Create (Info, PixelFormats.Gray8, null, pixels);
        }

        void ReadPalette ()
        {
            m_palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Rgb);
            m_input.Position = 0x20909;
        }
    }

    internal class UCgDecoder : BinaryImageDecoder
    {
        public UCgDecoder (IBinaryStream input, CgMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            using (var lz = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
            {
                /*
                int stride = (int)Info.Width * 2;
                var pixels = new byte[stride * (int)Info.Height];
                if (pixels.Length != lz.Read (pixels, 0, pixels.Length))
                    throw new InvalidFormatException();
                */
                int w2x = (int)Info.Width * 2;
                int stride = (w2x + 3) & ~3;
                var pixels = new byte[stride * (int)Info.Height];
                for (int dst = 0; dst < pixels.Length; dst += stride)
                {
                    lz.Read (pixels, dst, w2x);
                }
                return ImageData.Create (Info, PixelFormats.Bgr565, null, pixels, stride);
            }
        }
    }
}
