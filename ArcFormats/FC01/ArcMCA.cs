//! \file       ArcMCA.cs
//! \date       Sun Dec 06 19:12:34 2015
//! \brief      F&C Co. multi-frame image format.
//
// Copyright (C) 2015 by morkt
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

using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FC01
{
    internal class McaArchive : ArcFile
    {
        public readonly byte Key;
        public readonly int  BPP;
        public readonly BitmapPalette Palette;

        public McaArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key, int bpp = 24, BitmapPalette palette = null)
            : base (arc, impl, dir)
        {
            Key = key;
            BPP = bpp;
            Palette = palette;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class McaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MCA"; } }
        public override string Description { get { return "F&C Co. multi-frame image format"; } }
        public override uint     Signature { get { return 0x2041434D; } } // 'MCA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (0x10);
            int count = file.View.ReadInt32 (0x20);
            if (index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;

            int bpp = file.View.ReadInt32 (0x14);
            BitmapPalette palette = null;
            if (8 == bpp)
            {
                palette = ReadPalette (file, index_offset);
                index_offset += 0x400;
            }
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            long next_offset = file.View.ReadUInt32 (index_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (next_offset > file.MaxOffset || next_offset <= index_offset)
                    return null;
                index_offset += 4;
                var entry = new Entry
                {
                    Name    = string.Format ("{0}#{1:D4}", base_name, i),
                    Offset  = next_offset,
                    Type    = "image",
                };
                next_offset = i+1 == count ? file.MaxOffset : file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (entry.Size > 0x20)
                    dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            var options = Query<McgOptions> (arcStrings.MCAEncryptedNotice);
            return new McaArchive (file, this, dir, options.Key, bpp, palette);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var mca = (McaArchive)arc;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            try
            {
                return new McaDecoder (input, mca.Key, mca.BPP, mca.Palette);
            }
            catch
            {
                input.Dispose();
                throw;
            }
        }

        BitmapPalette ReadPalette (ArcView file, uint offset)
        {
            var palette = file.View.ReadBytes (offset, 0x400);
            int src = 0;
            var colors = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                colors[i] = Color.FromRgb (palette[src+2], palette[src+1], palette[src]);
                src += 4;
            }
            return new BitmapPalette (colors);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new McgOptions { Key = Settings.Default.MCGLastKey };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetMCG;
            if (null != w)
                Settings.Default.MCGLastKey = w.GetKey();
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMCG();
        }
    }

    internal sealed class McaDecoder : BinaryImageDecoder
    {
        byte                m_key;
        bool                m_compressed;
        int                 m_packed_size;
        int                 m_unpacked_size;
        BitmapPalette       m_palette;

        public McaDecoder (IBinaryStream input, byte key, int bpp, BitmapPalette palette) : base (input)
        {
            m_key = key;
            var header = m_input.ReadHeader (0x20);
            int method = header.ToInt32 (0);
            if (method < 0 || method > 1)
                throw new InvalidFormatException();
            m_compressed = method != 0;
            uint width  = header.ToUInt32 (0xC);
            uint height = header.ToUInt32 (0x10);
            m_packed_size = header.ToInt32 (0x14);
            m_unpacked_size = header.ToInt32 (0x18);
            Info = new ImageMetaData { Width = width, Height = height, BPP = bpp };
            m_palette = palette;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x20;
            var data = m_input.ReadBytes (m_packed_size);
            MrgOpener.Decrypt (data, 0, data.Length, m_key);
            if (m_compressed)
            {
                using (var input = new BinMemoryStream (data))
                using (var lzss = new MrgLzssReader (input, data.Length, m_unpacked_size))
                {
                    lzss.Unpack();
                    data = lzss.Data;
                }
            }
            int stride = ((int)Info.Width * Info.BPP / 8 + 3) & ~3;
            var format = 8 == Info.BPP ? PixelFormats.Indexed8 : PixelFormats.Bgr24;
            return ImageData.Create (Info, format, m_palette, data, stride);
        }
    }
}
