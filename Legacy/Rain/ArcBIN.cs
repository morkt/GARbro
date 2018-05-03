//! \file       ArcBIN.cs
//! \date       2017 Dec 03
//! \brief      Rain Software resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Text.RegularExpressions;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Rain
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/RAIN"; } }
        public override string Description { get { return "Rain Software resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex PackNameRe = new Regex (@"^pack(...)\.bin$", RegexOptions.IgnoreCase);

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            bool is_compressed = 0 == (count & 0x80000000);
            count &= 0x7FFFFFFF;
            if (!IsSaneCount (count))
                return null;
            var match = PackNameRe.Match (Path.GetFileName (file.Name));
            if (!match.Success)
                return null;
            var ext = match.Groups[1].Value;
            uint index_size = (uint)count * 12;
            if (index_size > file.View.Reserve (4, index_size))
                return null;
            uint index_offset = 4;
            uint data_offset = 4 + index_size;
            var dir = new List<Entry> (count);
            var seen_nums = new HashSet<uint>();
            for (int i = 0; i < count; ++i)
            {
                uint num = file.View.ReadUInt32 (index_offset);
                if (num > 0xFFFFFF || !seen_nums.Add (num))
                    return null;
                var name = string.Format ("{0:D5}.{1}", num, ext);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "SZDD"))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset+12, entry.Size-12);
            var lzss = new LzssStream (input);
            lzss.Config.FrameFill = 0x20;
            lzss.Config.FrameInitPos = 0xFF0;
            return lzss;
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (!entry.Name.HasExtension ("cgd"))
                return base.OpenImage (arc, entry);

            var input = OpenEntry (arc, entry);
            return new CgdImageDecoder (new BinaryStream (input, entry.Name));
        }
    }

    internal class CgdImageDecoder : BinaryImageDecoder
    {
        int     m_data_length;
        int     m_blocks_offset;
        int     m_rgb16_offset;
        int     m_rgb24_offset;

        int     m_blocks_w;
        int     m_blocks_h;
        int     m_stride;

        public CgdImageDecoder (IBinaryStream input) : base (input)
        {
            var header = m_input.ReadBytes (0x2C);
            Info = new ImageMetaData {
                Width = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = 24,
            };
            m_data_length = header.ToInt32 (0);
            m_blocks_offset = header.ToInt32 (0x20);
            m_rgb16_offset = header.ToInt32 (0x24);
            m_rgb24_offset = header.ToInt32 (0x28);
            m_blocks_w = (int)Info.Width / 8;
            m_blocks_h = (int)Info.Height / 8;
            m_stride = (int)Info.Width * 3;
        }

        protected override ImageData GetImageData ()
        {
            int[] blocks;
            byte[] pixels;
            if (m_blocks_offset != 0)
            {
                blocks = new int[m_blocks_w * m_blocks_h * 2];
                for (int i = 0; i < blocks.Length; ++i)
                {
                    blocks[i] = m_input.ReadInt32();
                }
                m_input.ReadBytes (m_rgb24_offset-m_rgb16_offset);
                pixels = UnpackBlocks (blocks);
            }
            else
            {
                m_input.ReadBytes (m_rgb24_offset-m_rgb16_offset);
                pixels = m_input.ReadBytes (m_data_length-m_rgb24_offset);
            }
            return ImageData.Create (Info, PixelFormats.Bgr24, null, pixels, m_stride);
        }

        byte[] UnpackBlocks (int[] blocks)
        {
            var output = new byte[m_stride * (int)Info.Height];
            int i = 0;
            for (int y = 0; y < m_blocks_h; ++y)
            {
                int dst = y * 8 * m_stride;
                for (int x = 0; x < m_blocks_w; )
                {
                    int count = (blocks[i] >> 8) & 0xFF;
                    int row_length = 0;
                    switch (blocks[i] >> 16)
                    {
                    case 0: row_length = 0; break;
                    case 1: row_length = 24; break;
                    case 2:
                    case 3: row_length = 32; break;
                    }
                    for (int j = 0; j < count; ++j)
                    {
                        if ((blocks[i] >> 16) != 0)
                        {
                            m_input.Read (output, dst, row_length);
                            dst += row_length;
                        }
                        i += 2;
                    }
                    if (row_length != 0)
                    {
                        m_input.Read (output, dst, 7 * count * row_length);
                        dst += 7 * count * row_length;
                    }
                    x += count;
                }
            }
            return output;
        }
    }
}
