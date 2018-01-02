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
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Parsley
{
    [Export(typeof(ArchiveFormat))]
    public class CgOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CG/PARSLEY/2"; } }
        public override string Description { get { return "Software House Parsley CG archive"; } }
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
                var entry = FormatCatalog.Instance.Create<Entry> (name);
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
            bool is_cg = arc_name == "CG";

            using (var index = file.CreateStream())
            {
                index.Position = 4;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString();
                    if (string.IsNullOrWhiteSpace (name) || name.Length > 0x100)
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.ReadUInt32();
                    if (entry.Offset >= file.MaxOffset || entry.Offset < index.Position)
                        return null;
                    dir.Add (entry);
                }
                for (int i = 0; i < count; ++i)
                {
                    var entry = dir[i];
                    long next_offset = i+1 < count ? dir[i+1].Offset : file.MaxOffset;
                    entry.Size = (uint)(next_offset - entry.Offset);
                    if (is_cg)
                        entry.Type = "image";
                }
                if (is_cg)
                {
                    var palette_entry = dir.FirstOrDefault (e => e.Name.Equals ("Palette", StringComparison.InvariantCultureIgnoreCase));
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
            var info = new CgMetaData {
                Width = arc.File.View.ReadUInt32 (entry.Offset),
                Height = arc.File.View.ReadUInt32 (entry.Offset+4),
                BPP = 8,
                HasPalette = type == 1,
            };
            var cg_arc = arc as CgArchive;
            if (cg_arc != null)
                info.DefaultPalette = cg_arc.DefaultPalette;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
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
}
