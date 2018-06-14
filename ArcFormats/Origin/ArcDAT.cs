//! \file       ArcDAT.cs
//! \date       2018 Jun 02
//! \brief      origin engine resource archive.
//
// Copyright (C) 2018 by morkt
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
using GameRes.Utility;

// [080523][Plum Zero] Kankeizu

namespace GameRes.Formats.Origin
{
    [Export(typeof(ArchiveFormat))]
    public class HedDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/HED"; } }
        public override string Description { get { return "origin engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension ("DAT"))
                return null;
            var hed_name = Path.ChangeExtension (file.Name, "HED");
            if (!VFS.FileExists (hed_name))
                return null;
            using (var hed = VFS.OpenBinaryStream (hed_name))
            {
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                var dir = new List<Entry>();
                var name_buffer = new byte[0x100];
                while (hed.PeekByte() != -1)
                {
                    int name_length = hed.ReadUInt8();
                    string name;
                    if (name_length != 0)
                    {
                        if (hed.Read (name_buffer, 0, name_length) != name_length)
                            return null;
                        for (int i = 0; i < name_length; ++i)
                            name_buffer[i] ^= 0xFF;
                        name = Binary.GetCString (name_buffer, 0, name_length);
                    }
                    else
                    {
                        name = string.Format ("{0}#{1:D4}", base_name, dir.Count);
                    }
                    var entry = new Entry {
                        Name = name,
                        Offset = hed.ReadUInt32(),
                    };
                    if (entry.Offset > file.MaxOffset)
                        return null;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                AdjustSizes (dir, file.MaxOffset);
                DetectFileTypes (dir, file);
                return new ArcFile (file, this, dir);
            }
        }

        void AdjustSizes (List<Entry> dir, long arc_length)
        {
            var last = dir[0];
            for (int i = 1; i < dir.Count; ++i)
            {
                var next = dir[i];
                last.Size = (uint)(next.Offset - last.Offset);
                last = next;
            }
            last.Size = (uint)(arc_length - last.Offset);
        }

        void DetectFileTypes (List<Entry> dir, ArcView file)
        {
            bool is_mask = VFS.IsPathEqualsToFileName (file.Name, "MASK.DAT");
            var buffer = new byte[0x11];
            foreach (var entry in dir)
            {
                file.View.Read (entry.Offset, buffer, 0, 0x11);
                if (buffer.AsciiEqual (0xD, "OggS"))
                {
                    entry.ChangeType (OggAudio.Instance);
                    entry.Offset += 0xD;
                    entry.Size -= 0xD;
                }
                else if (is_mask || buffer[0] <= 1 && buffer[1] > 0 && buffer[1] <= 3)
                    entry.Type = "image";
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            OrgMetaData info;
            if (VFS.IsPathEqualsToFileName (arc.File.Name, "MASK.DAT"))
            {
                info = new OrgMetaData {
                    Width = arc.File.View.ReadUInt32 (entry.Offset),
                    Height = arc.File.View.ReadUInt32 (entry.Offset+4),
                    BPP = 8,
                    IsMask = true,
                };
            }
            else
            {
                byte has_alpha = arc.File.View.ReadByte (entry.Offset);
                byte type      = arc.File.View.ReadByte (entry.Offset+1);
                if (has_alpha > 1 || type < 1 || type > 3)
                    return base.OpenImage (arc, entry);
                info = new OrgMetaData {
                    Width = arc.File.View.ReadUInt16 (entry.Offset+2),
                    Height = arc.File.View.ReadUInt16 (entry.Offset+4),
                    HasAlpha = has_alpha != 0,
                    Method = type,
                    BPP = 32,
                };
            }
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new OrgImageDecoder (input, info);
        }
    }

    internal class OrgMetaData : ImageMetaData
    {
        public bool IsMask;
        public bool HasAlpha;
        public byte Method;
    }

    internal class OrgImageDecoder : BinaryImageDecoder
    {
        OrgMetaData     m_info;
        int             m_width;
        int             m_height;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }

        public OrgImageDecoder (IBinaryStream input, OrgMetaData info) : base (input, info)
        {
            m_info = info;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
        }

        byte[]  m_symbol_table;

        protected override ImageData GetImageData ()
        {
            if (m_info.IsMask)
                return GetMaskData();

            m_input.Position = 6;
            int pixel_size;
            if (1 == m_info.Method || 2 == m_info.Method)
            {
                int colors = m_input.ReadUInt16();
                Palette = ImageFormat.ReadPalette (m_input.AsStream, colors, PaletteFormat.Bgr);
                pixel_size = 1;
            }
            else if (3 == m_info.Method)
            {
                pixel_size = 3;
            }
            else
                throw new InvalidFormatException();

            if (2 == m_info.Method)
                m_symbol_table = Enumerable.Range (0, 0x100).Select (x => (byte)x).ToArray();
            else if (3 == m_info.Method)
                m_symbol_table = m_input.ReadBytes (0x100);

            int plane_length = m_width * m_height;
            var planes = new byte[pixel_size * plane_length];
            int packed_length = m_input.ReadInt32();
            long data_end = m_input.Position + packed_length;
            if (m_info.Method > 1)
            {
                using (var bits = new LsbBitStream (m_input.AsStream, true))
                    UnpackHuffman (bits, planes);
            }
            else
            {
                UnpackLz (planes);
            }

            byte[] alpha = null;
            if (m_info.HasAlpha)
            {
                m_input.Position = data_end;
                int w = m_input.ReadInt32();
                int h = m_input.ReadInt32();
                int method = m_input.ReadByte();
                if (w == m_width && h == m_height && (1 == method || 2 == method))
                {
                    alpha = new byte[plane_length];
                    if (1 == method)
                        UnpackRle (alpha);
                    else
                        UnpackAlphaV2 (alpha);
                    pixel_size = 4;
                }
            }

            byte[] pixels;
            if (3 == m_info.Method)
            {
                int b = 0;
                int g = plane_length;
                int r = plane_length * 2;
                PaethFilter (planes, b);
                PaethFilter (planes, g);
                PaethFilter (planes, r);
                pixels = new byte[pixel_size * plane_length];
                int dst = 0;
                for (int src = 0; src < plane_length; ++src)
                {
                    pixels[dst  ] = planes[b + src];
                    pixels[dst+1] = planes[g + src];
                    pixels[dst+2] = planes[r + src];
                    if (alpha != null)
                        pixels[dst+3] = alpha[src];
                    dst += pixel_size;
                }
            }
            else if (alpha != null)
            {
                pixels = new byte[pixel_size * plane_length];
                int dst = 0;
                for (int src = 0; src < plane_length; ++src)
                {
                    var color = Palette.Colors[planes[src]];
                    pixels[dst++] = color.B;
                    pixels[dst++] = color.G;
                    pixels[dst++] = color.R;
                    pixels[dst++] = alpha[src];
                }
                Palette = null;
            }
            else
            {
                pixels = planes;
            }

            if (1 == pixel_size)
                Format = PixelFormats.Indexed8;
            else if (3 == pixel_size)
                Format = PixelFormats.Bgr24;
            else
                Format = PixelFormats.Bgra32;

            int stride = m_width * pixel_size;
            return ImageData.Create (m_info, Format, Palette, pixels, stride);
        }

        internal ImageData GetMaskData ()
        {
            Format = PixelFormats.Gray8;
            m_input.Position = 12;
            int method = m_input.ReadByte();
            var pixels = new byte[m_width * m_height];
            if (1 == method)
                UnpackRle (pixels);
            else if (2 == method)
                UnpackAlphaV2 (pixels);
            else
                throw new InvalidFormatException();

            return ImageData.Create (m_info, Format, null, pixels, m_width);
        }

        struct LzNode
        {
            public  int Offset;
            public  int Length;
        }

        void UnpackLz (byte[] output)
        {
            var tree = new LzNode[0x10000];
            int node_count = 0;
            int dst = 0;
            int ctl = 1;
            while (dst < output.Length)
            {
                if (1 == ctl)
                    ctl = m_input.ReadUInt8() | 0x100;
                int count = 0;
                if ((ctl & 1) != 0)
                {
                    int index;
                    if (node_count < 0x100)
                        index = m_input.ReadUInt8();
                    else
                        index = m_input.ReadUInt16();

                    count = tree[index].Length;
                    Buffer.BlockCopy (output, tree[index].Offset, output, dst, count);
                }
                int symbol = m_input.ReadByte();
                if (-1 == symbol)
                    break;
                output[dst + count++] = (byte)symbol;
                if (node_count < tree.Length)
                {
                    tree[node_count].Offset = dst;
                    tree[node_count].Length = count;
                    ++node_count;
                }
                dst += count;
                ctl >>= 1;
            }
        }

        struct HuffmanNode
        {
            public int  Parent;
            public byte Value;
        }

        void UnpackHuffman (IBitStream input, byte[] output)
        {
            var tree = new HuffmanNode[0x10000];
            var buf = new byte[0x10000];
            int node_count = 0;
            int dst = 0;
            while (dst < output.Length)
            {
                int index = -1;
                int bit = input.GetNextBit();
                if (-1 == bit)
                    break;

                if (bit != 0)
                {
                    int node_bits = node_count == tree.Length ? node_count - 1 : node_count;
                    index = input.GetNextBit();
                    for (int shift = 1; node_bits > 1; ++shift)
                    {
                        index |= input.GetNextBit() << shift;
                        node_bits >>= 1;
                    }
                    int chunk_length = 0;
                    int curr_index = index;
                    do {
                        buf[chunk_length++] = tree[curr_index].Value;
                        curr_index          = tree[curr_index].Parent;
                    }
                    while (curr_index != -1);

                    while (chunk_length --> 0)
                        output[dst++] = buf[chunk_length];
                }

                int bits_count = 2;
                while (input.GetNextBit() > 0)
                    bits_count++;

                int c = 0;
                while (bits_count --> 0)
                    c |= input.GetNextBit() << bits_count;
                if (c < 0)
                    break;

                byte symbol = m_symbol_table[c];
                output[dst++] = symbol;

                if (node_count < tree.Length)
                {
                    tree[node_count].Parent = index;
                    tree[node_count].Value  = symbol;
                    node_count++;
                }
            }
        }

        void UnpackRle (byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                byte symbol = m_input.ReadUInt8();
                int count = Math.Min (m_input.ReadUInt8(), output.Length - dst);
                while (count --> 0)
                    output[dst++] = symbol;
            }
        }

        void UnpackAlphaV2 (byte[] output)
        {
            int dst = 0;
            var table = new byte[4];
            m_input.Read (table, 1, 3);
            int ctl = 1;
            byte prev = 0;
            while (dst < output.Length)
            {
                if (1 == ctl)
                    ctl = m_input.ReadUInt8() | 0x100;

                int i = ctl & 3;
                byte diff;
                if (0 == i)
                    diff = m_input.ReadUInt8();
                else
                    diff = table[i];
                prev -= diff;
                output[dst++] = prev;
                ctl >>= 2;
            }
        }

        void PaethFilter (byte[] data, int pos)
        {
            for (int x = 1; x < m_width; ++x)
                data[pos+x] += data[pos+x-1];
            int row = pos;
            for (int y = 1; y < m_height; ++y)
            {
                int prev_row = row;
                row += m_width;
                data[row] += data[prev_row];

                for (int x = 1; x < m_width; ++x)
                {
                    data[row+x] += PaethPredictor (data[row+x-1], data[prev_row+x], data[prev_row+x-1]);
                }
            }
        }

        byte PaethPredictor (byte px, byte py, byte pxy)
        {
            int pa = Math.Abs (py - pxy);
            int pb = Math.Abs (px - pxy);
            int pc = Math.Abs (py + px - 2 * pxy);
            if (pc < pa && pc < pb)
                return pxy;
            else if (pb < pa)
                return py;
            else
                return px;
        }
    }
}
