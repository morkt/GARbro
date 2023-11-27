//! \file       ImageBIZ.cs
//! \date       2023 Sep 30
//! \brief      ADVIZ engine image format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [970829][Ange] Coin

namespace GameRes.Formats.Adviz
{
    [Export(typeof(ImageFormat))]
    public class BizFormat : ImageFormat
    {
        public override string         Tag => "BIZ";
        public override string Description => "ADVIZ engine image format";
        public override uint     Signature => 0;

        const byte DefaultKey = 0x39;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".BIZ"))
                return null;
            var header = file.ReadHeader (4);
            uint width  = header.ToUInt16 (0);
            uint height = header.ToUInt16 (2);
            if (width * height + 4 != file.Length)
                return null;
            return new ImageMetaData {
                Width = width,
                Height = height,
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var palette = ReadPalette (file.Name, 0x300, (pal, off) => ReadPalette (pal, off, 0x100, PaletteFormat.Rgb));
            if (null == palette)
                throw new FileNotFoundException ("Unable to retrieve palette.");
            file.Position = 4;
            var pixels = file.ReadBytes (info.iWidth * info.iHeight);
            byte key = DefaultKey;
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] ^= key;
                key += pixels[i];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, info.iWidth);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BizFormat.Write not implemented");
        }

        internal delegate BitmapPalette PaletteReader (ArcView file, int offset);

        static readonly Regex TachieRe = new Regex (@"^(T[^._]+_)[2-9][^.]*\.GIZ$", RegexOptions.Compiled);

        internal static BitmapPalette ReadPalette (string base_name, int pal_size, PaletteReader read_pal)
        {
            var dir_name = Path.GetDirectoryName (base_name);
            var grp_tbl_name = Path.Combine (dir_name, @"..\GRP_TBL.SYS");
            var plt_tbl_name = Path.Combine (dir_name, @"..\PLT_TBL.SYS");
            if (!File.Exists (grp_tbl_name) || !File.Exists (plt_tbl_name))
                return null;
            int index = 0;
            uint grp_size = 0;
            base_name = Path.GetFileName (base_name).ToUpperInvariant();
            var name = base_name;
            var ext  = Path.GetExtension (name).TrimStart('.');
            var match = TachieRe.Match (name);
            if (match.Success)
                name = match.Groups[1].Value + "1";
            else
                name = Path.GetFileNameWithoutExtension (name);
            if (name.Length < 8)
                name += ' ';
            using (var grp = new ArcView (grp_tbl_name))
            {
                grp_size = (uint)grp.MaxOffset;
                int pos = 0;
                while (pos + 12 <= grp.MaxOffset)
                {
                    if (grp.View.AsciiEqual (pos, name) &&
                        grp.View.AsciiEqual (pos+8, ext))
                    {
                        break;
                    }
                    ++index;
                    pos += 12;
                }
                if (pos >= grp.MaxOffset)
                    return null;
            }
            using (var pal = new ArcView (plt_tbl_name))
            {
                uint plt_size = (uint)pal.MaxOffset;
                var id = new GrpIdentifier (grp_size, plt_size);
                IGrpMapper mapper;
                if (!GrpMap.TryGetValue (id, out mapper))
                    mapper = new DirectMapper();
                index = mapper.GetPaletteIndex (index, base_name);
                int pal_offset = index * pal_size;
                if (pal_offset + pal_size > pal.MaxOffset)
                {
                    int count = (int)(pal.MaxOffset / pal_size) - 1;
                    pal_offset = count * pal_size;
                }
                return read_pal (pal, pal_offset);
            }
        }

        static readonly Dictionary<GrpIdentifier, IGrpMapper> GrpMap = new Dictionary<GrpIdentifier, IGrpMapper> {
            { new GrpIdentifier (1584, 139008), new GrpShiftMapper (52) },
            { new GrpIdentifier (2160, 12288),
                new GrpNameMapper { NameMap = new Dictionary<string, int> {
                    { "BG01.BIZ", 14 },
                    { "BG02.BIZ", 8 },
                    { "BG03.BIZ", 8 },
                    { "BG04.BIZ", 8 },
                    { "BG05.BIZ", 8 },
                    { "BG06.BIZ", 8 },
                    { "BG07.BIZ", 8 },
                    { "BG08.BIZ", 8 },
                    { "BG09.BIZ", 8 },
                    { "BG10.BIZ", 8 },
                    { "BG11.BIZ", 8 },
                    { "BG12.BIZ", 8 },
                    { "BG13.BIZ", 8 },
                    { "BG14.BIZ", 8 },
                    { "BG15.BIZ", 8 },
                    { "BG16.BIZ", 8 },
                    { "BG17.BIZ", 8 },
                    { "BG18.BIZ", 8 },
                    { "BG19.BIZ", 8 },
                    { "CA01.BIZ", 12 },
                    { "CA02.BIZ", 12 },
                    { "CA03.BIZ", 4 },
                    { "CA04.BIZ", 8 },
                    { "CA05.BIZ", 8 },
                    { "CA06.BIZ", 8 },
                    { "CA07.BIZ", 8 },
                    { "CA08.BIZ", 8 },
                    { "CA09.BIZ", 8 },
                    { "CA10.BIZ", 8 },
                    { "CA11.BIZ", 8 },
                    { "CA12.BIZ", 8 },
                    { "CA13.BIZ", 8 },
                    { "CA14.BIZ", 8 },
                    { "CA15.BIZ", 8 },
                    { "CA16.BIZ", 8 },
                    { "CA17.BIZ", 8 },
                    { "CA18.BIZ", 8 },
                    { "CA19.BIZ", 8 },
                    { "CA20.BIZ", 8 },
                    { "CA21.BIZ", 8 },
                    { "CA22.BIZ", 8 },
                    { "CA23.BIZ", 8 },
                    { "CA24.BIZ", 8 },
                    { "CA25.BIZ", 8 },
                    { "CA26.BIZ", 8 },
                    { "CA27.BIZ", 8 },
                    { "CA28.BIZ", 8 },
                    { "CA29.BIZ", 8 },
                    { "CA30.BIZ", 8 },
                    { "CA31.BIZ", 8 },
                    { "CA32.BIZ", 8 },
                    { "CA33.BIZ", 8 },
                    { "CA34.BIZ", 8 },
                    { "CA35.BIZ", 8 },
                    { "CA36.BIZ", 8 },
                    { "CA37.BIZ", 8 },
                    { "CA38.BIZ", 8 },
                    { "CA39.BIZ", 8 },
                    { "CA40.BIZ", 8 },
                    { "CA41.BIZ", 8 },
                    { "CA42.BIZ", 8 },
                    { "CA43.BIZ", 8 },
                    { "CA44.BIZ", 8 },
                    { "CA45.BIZ", 8 },
                    { "CA46.BIZ", 8 },
                    { "CA47.BIZ", 8 },
                    { "CA48.BIZ", 8 },
                    { "CA49.BIZ", 8 },
                    { "CA50.BIZ", 8 },
                    { "CA51.BIZ", 8 },
                    { "CA52.BIZ", 8 },
                    { "CA53.BIZ", 8 },
                    { "CA54.BIZ", 8 },
                    { "CA55.BIZ", 8 },
                    { "CA56.BIZ", 8 },
                    { "CA57.BIZ", 8 },
                    { "CA58.BIZ", 8 },
                    { "CA59.BIZ", 8 },
                    { "CA60.BIZ", 8 },
                    { "E02.BIZ", 8 },
                    { "E03.BIZ", 8 },
                    { "E04.BIZ", 8 },
                    { "E05.BIZ", 8 },
                    { "E06.BIZ", 8 },
                    { "E07.BIZ", 8 },
                    { "E08.BIZ", 8 },
                    { "E09.BIZ", 8 },
                    { "E10.BIZ", 8 },
                    { "E11.BIZ", 8 },
                    { "E12.BIZ", 8 },
                    { "E13.BIZ", 8 },
                    { "E14.BIZ", 8 },
                    { "E15.BIZ", 8 },
                    { "E16.BIZ", 8 },
                    { "E17.BIZ", 8 },
                    { "E18.BIZ", 8 },
                    { "END.BIZ", 6 },
                    { "IPL.BIZ", 12 },
                    { "S01.BIZ", 8 },
                    { "S02.BIZ", 8 },
                    { "S03.BIZ", 8 },
                    { "S04.BIZ", 8 },
                    { "S05.BIZ", 8 },
                    { "S06.BIZ", 8 },
                    { "S07.BIZ", 8 },
                    { "S08.BIZ", 8 },
                    { "S09.BIZ", 8 },
                    { "S10.BIZ", 8 },
                    { "S11.BIZ", 8 },
                    { "S12.BIZ", 8 },
                    { "S13.BIZ", 8 },
                    { "S14.BIZ", 8 },
                    { "S15.BIZ", 8 },
                    { "S16.BIZ", 8 },
                    { "S17.BIZ", 8 },
                    { "S18.BIZ", 8 },
                    { "S19.BIZ", 8 },
                    { "S20.BIZ", 8 },
                    { "S21.BIZ", 8 },
                    { "S22.BIZ", 8 },
                    { "S23.BIZ", 8 },
                    { "S24.BIZ", 8 },
                    { "S25.BIZ", 8 },
                    { "S26.BIZ", 8 },
                    { "S27.BIZ", 8 },
                    { "S28.BIZ", 8 },
                    { "S29.BIZ", 8 },
                    { "S30.BIZ", 8 },
                    { "S31.BIZ", 8 },
                    { "S32.BIZ", 8 },
                    { "S33.BIZ", 8 },
                    { "S34.BIZ", 8 },
                    { "S35.BIZ", 8 },
                    { "S36.BIZ", 8 },
                    { "S37.BIZ", 8 },
                    { "S38.BIZ", 8 },
                    { "S39.BIZ", 8 },
                    { "S40.BIZ", 8 },
                    { "S41.BIZ", 8 },
                    { "S42.BIZ", 8 },
                    { "S43.BIZ", 8 },
                    { "S44.BIZ", 8 },
                    { "S45.BIZ", 8 },
                    { "S46.BIZ", 8 },
                    { "S47.BIZ", 8 },
                    { "S48.BIZ", 8 },
                    { "S49.BIZ", 8 },
                    { "T01.BIZ", 8 },
                    { "T02.BIZ", 8 },
                    { "T03.BIZ", 8 },
                    { "WAKU1.BIZ", 8 },
                    { "WAKU2.BIZ", 8 },
                } } },
        };
    }

    public struct GrpIdentifier
    {
        public  uint    GrpSize;
        public  uint    PltSize;

        public GrpIdentifier (uint grp_size, uint plt_size)
        {
            GrpSize = grp_size;
            PltSize = plt_size;
        }

        public override int GetHashCode ()
        {
            return (int)((GrpSize + 1) * (PltSize + 1));
        }

        public override bool Equals (object obj)
        {
            if (null == obj)
                return false;
            var other = (GrpIdentifier)obj;
            return this.GrpSize == other.GrpSize && this.PltSize == other.PltSize;
        }
    }

    internal interface IGrpMapper
    {
        int GetPaletteIndex (int id, string name);
    }

    internal class DirectMapper : IGrpMapper
    {
        public int GetPaletteIndex (int id, string name)
        {
            return id;
        }
    }

    internal class GrpShiftMapper : IGrpMapper
    {
        int m_shift;

        public GrpShiftMapper (int shift)
        {
            m_shift = shift;
        }

        public int GetPaletteIndex (int id, string name)
        {
            return id + m_shift;
        }
    }

    internal class GrpNameMapper : IGrpMapper
    {
        public Dictionary<string, int> NameMap;

        public int GetPaletteIndex (int id, string name)
        {
            int index;
            if (NameMap.TryGetValue (name, out index))
                return index;
            return id;
        }
    }
}
