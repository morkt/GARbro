//! \file       ImageMAG.cs
//! \date       2017 Dec 28
//! \brief      Otemoto image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Otemoto
{
    internal class MagMetaData : ImageMetaData
    {
        public int  PaletteOffset;
        public int  BitsOffset;
        public int  Data1Offset;
        public int  Data1Length;
        public int  Data2Offset;
        public int  Data2Length;
    }

    [Export(typeof(ImageFormat))]
    public class MagFormat : ImageFormat
    {
        public override string         Tag { get { return "MAG/MAKI02"; } }
        public override string Description { get { return "Otemoto image format"; } }
        public override uint     Signature { get { return 0x494B414D; } } // 'MAKI02'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            if (!header.AsciiEqual ("MAKI02  "))
                return null;
            int header_pos = 8;
            for ( ; header_pos < header.Length; ++header_pos)
            {
                if (header[header_pos] == 0x1A)
                    break;
            }
            if (header_pos++ == header.Length)
                return null;
            header = file.ReadHeader (header_pos+0x20);
            int x = header.ToUInt16 (header_pos+4);
            int y = header.ToUInt16 (header_pos+6);
            int width  = header.ToUInt16 (header_pos+8) - x + 1;
            int height = header.ToUInt16 (header_pos+10) - y + 1;
            int bpp  = (0 != (header[header_pos+3] & 0x80)) ? 8 : 4;
            return new MagMetaData {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = x,
                OffsetY = y,
                BPP = bpp,
                PaletteOffset = header_pos + 0x20,
                BitsOffset = header_pos + header.ToInt32 (header_pos + 0xC),
                Data1Offset = header_pos + header.ToInt32 (header_pos + 0x10),
                Data1Length = header_pos + header.ToInt32 (header_pos + 0x14),
                Data2Offset = header_pos + header.ToInt32 (header_pos + 0x18),
                Data2Length = header_pos + header.ToInt32 (header_pos + 0x1C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new MakiReader (file, (MagMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MagFormat.Write not implemented");
        }
    }

    internal sealed class MakiReader
    {
        IBinaryStream   m_input;
        MagMetaData     m_info;
        int             m_stride;
        ushort[]        m_output;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get { return m_stride; } }
        public Array            Data { get { return m_output; } }

        public MakiReader (IBinaryStream file, MagMetaData info)
        {
            m_input = file;
            m_info  = info;
            m_stride = (((int)m_info.Width * m_info.BPP + 31) >> 5) << 2;
            if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else
                Format = PixelFormats.Indexed4;
            PrepareOffsetsTable();
        }

        public Array Unpack ()
        {
            m_input.Position = m_info.PaletteOffset;
            Palette = ReadPalette();

            m_input.Position = m_info.BitsOffset;
            var ctl_bits = m_input.ReadBytes (m_info.Data1Offset - m_info.BitsOffset);
            int bits_src = 0;

            m_input.Position = m_info.Data1Offset;
            var data1 = m_input.ReadBytes (m_info.Data1Length);
            int src1 = 0;

            m_input.Position = m_info.Data2Offset;
            int height = (int)m_info.Height;
            m_output = new ushort[height * m_stride / 2];
            int w_blocks = (int)m_info.Width / (32 / m_info.BPP);
            var cbuf = new byte[w_blocks];
            byte bit_mask = 0;
            byte bits = 0;
            for (int y = 0; y < height; ++y)
            {
                int dst = m_stride * y / 2;
                for (int x = 0; x < w_blocks; ++x)
                {
                    bit_mask >>= 1;
                    if (0 == bit_mask)
                    {
                        bits = ctl_bits[bits_src++];
                        bit_mask = 0x80;
                    }
                    if (0 != (bit_mask & bits))
                        cbuf[x] ^= data1[src1++];
                    RestorePixels (dst++, cbuf[x] >> 4);
                    RestorePixels (dst++, cbuf[x] & 0xF);
                }
            }
            return m_output;
        }

        void RestorePixels (int dst, int code)
        {
            ushort val;
            if (code != 0)
                val = m_output[dst + m_offsets[code]];
            else
                val = m_input.ReadUInt16();
            m_output[dst] = val;
        }

        BitmapPalette ReadPalette ()
        {
            int colors = 1 << m_info.BPP;
            var palette_data = m_input.ReadBytes (colors * 3);
            var color_map = new Color[colors];
            int src = 0;
            for (int i = 0; i < colors; ++i)
            {
                color_map[i] = Color.FromRgb (palette_data[src+1], palette_data[src], palette_data[src+2]);
                src += 3;
            }
            return new BitmapPalette (color_map);
        }

        int[]   m_offsets = new int[16];

        static readonly sbyte[] x_offs = { 0, 2, 4, 8, 0, 2, 0, 2, 4, 0, 2, 4, 0, 2, 4, 0 };
        static readonly sbyte[] y_offs = { 0, 0, 0, 0, -1, -1, -2, -2, -2, -4, -4, -4, -8, -8, -8, -16 };

        void PrepareOffsetsTable ()
        {
            for (int i = 0; i < 16; ++i)
            {
                int y = y_offs[i];
                int x = x_offs[i];
                m_offsets[i] = (m_stride * y - x) >> 1;
            }
        }
    }
}
