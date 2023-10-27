//! \file       ImageMAI3.cs
//! \date       2023 Oct 23
//! \brief      Izumi engine image format (PC-98).
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Izumi
{
    internal class Mai3MetaData : ImageMetaData
    {
        public int  DataOffset;
        public bool HasPalette;
    }

    [Export(typeof(ImageFormat))]
    public class Mai3Format : ImageFormat
    {
        public override string         Tag => "MI3";
        public override string Description => "Izumi engine image format";
        public override uint     Signature => 0x3049414D; // 'MAI03'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (14);
            if (!header.AsciiEqual ("MAI03\x1A"))
                return null;
            return new Mai3MetaData {
                Width  = (uint)(header.ToUInt16 (8) << 3),
                Height = header.ToUInt16 (0xA),
                BPP = 4,
                DataOffset = header.ToUInt16 (0xC) & 0x7FFF,
                HasPalette = (header[0xD] & 0x80) != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Mai3Reader (file, (Mai3MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mai3Format.Write not implemented");
        }
    }

    internal class Mai3Reader
    {
        IBinaryStream   m_input;
        Mai3MetaData    m_info;

        public Mai3Reader (IBinaryStream input, Mai3MetaData info)
        {
            m_input = input;
            m_info = info;
        }

        ushort[] m_buffer;
        int m_output_stride;
        byte[] m_output;

        public ImageData Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            BitmapPalette palette = null;
            if (m_info.HasPalette)
                palette = ReadPalette();
            else
                palette = BitmapPalettes.Gray16;
            m_buffer = new ushort[0x6D0];
            m_output_stride = m_info.iWidth >> 1;
            m_output = new byte[m_output_stride * m_info.iHeight];
            InitPixels();
            InitBitReader();
            int output_dst = 0;
            int stride = m_info.iWidth >> 3;
            int x = stride >> 1;
            while (x --> 0)
            {
                MoveBuffer();
                UnpackLine (0x1C0);
                UnpackLine (0x10);
                MoveBuffer();
                UnpackLine (0x1C0);
                UnpackLine (0x10);
                CopyOutput (output_dst);
                output_dst += 8;
            }
            if ((stride & 1) != 0)
            {
                MoveBuffer();
                UnpackLine (0x1C0);
                UnpackLine (0x10);
                CopyOutput (output_dst, 1);
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, m_output, m_output_stride);
        }

        void UnpackLine (int dst)
        {
            int height = m_info.iHeight;
            while (height > 0)
            {
                if (GetNextBit() == 0)
                {
                    int offset;
                    if (GetNextBit() != 0)
                        offset = 0;
                    else if (GetNextBit() != 0)
                        offset = 0x1B0;
                    else
                        offset = 0x360;
                    if (0 == offset || GetNextBit() != 0)
                    {
                        if (GetNextBit() != 0)
                            offset = -1;
                        else if (GetNextBit() != 0)
                            offset -= 2;
                        else if (GetNextBit() != 0)
                            offset -= 4;
                        else if (GetNextBit() != 0)
                            offset -= 8;
                        else
                            offset -= 0x10;
                    }
                    else if (GetNextBit() == 0)
                    {
                        if (GetNextBit() != 0)
                            offset += 2;
                        else if (GetNextBit() != 0)
                            offset += 4;
                        else if (GetNextBit() != 0)
                            offset += 8;
                        else
                            offset += 0x10;
                    }
                    int length = GetCount (8);
                    int count = 1;
                    for (int j = 0; j < length; ++j)
                        count = count << 1 | GetNextBit();
                    count += 1;
                    int src = dst + offset;
                    height -= count;
                    while (count --> 0)
                        m_buffer[dst++] = m_buffer[src++];
                }
                else
                {
                    ushort px = m_buffer[dst + 0x1B0];
                    int prev = (px >> 8) & 1;
                    prev <<= 1;
                    prev |= (px >> 12) & 1;
                    prev <<= 1;
                    prev |= px & 1;
                    prev <<= 1;
                    prev |= (px >> 4) & 1;

                    byte n0 = GetPixel ((byte)prev);
                    byte n1 = GetPixel (n0);
                    byte n2 = GetPixel (n1);
                    byte n3 = GetPixel (n2);

                    px  = m_patterns[0,n3];
                    px |= m_patterns[1,n2];
                    px |= m_patterns[2,n1];
                    px |= m_patterns[3,n0];

                    m_buffer[dst++] = px;
                    --height;
                }
            }
        }

        static readonly ushort[,] m_patterns = {
            { 0, 0x10, 1, 0x11, 0x1000, 0x1010, 0x1001, 0x1011, 0x100, 0x110, 0x101, 0x111, 0x1100, 0x1110, 0x1101, 0x1111 },
            { 0, 0x20, 2, 0x22, 0x2000, 0x2020, 0x2002, 0x2022, 0x200, 0x220, 0x202, 0x222, 0x2200, 0x2220, 0x2202, 0x2222 },
            { 0, 0x40, 4, 0x44, 0x4000, 0x4040, 0x4004, 0x4044, 0x400, 0x440, 0x404, 0x444, 0x4400, 0x4440, 0x4404, 0x4444 },
            { 0, 0x80, 8, 0x88, 0x8000, 0x8080, 0x8008, 0x8088, 0x800, 0x880, 0x808, 0x888, 0x8800, 0x8880, 0x8808, 0x8888 },
        };

        byte GetPixel (byte prev)
        {
            int count = GetCount (15);
            prev <<= 4;
            prev += 0xF;
            int src = prev - count;
            int dst = src;
            byte al = m_pixels[src++];
            if (count > 0)
            {
                while (count --> 0)
                    m_pixels[dst++] = m_pixels[src++];
                m_pixels[dst] = al;
            }
            return al;
        }

        int GetCount (int limit)
        {
            int count = 0;
            while (count < limit && GetNextBit() == 0)
                ++count;
            return count;
        }

        void MoveBuffer ()
        {
            Buffer.BlockCopy (m_buffer, 0x20, m_buffer, 0x6E0, 0x360 << 1);
        }

        void CopyOutput (int dst_line, int rows = 2)
        {
            int src = 0x10;
            int height = m_info.iHeight;
            for (int y = 0; y < height; ++y)
            {
                ushort cx = m_buffer[src + 0x510];
                ushort dx = m_buffer[src + 0x360];
                ushort bx = m_buffer[src + 0x1B0];
                ushort ax = m_buffer[src++];

                int b0 = bx <<  8 & 0xF000 | ax << 4 & 0x0F00 | cx      & 0x00F0 | dx >>  4 & 0xF;
                int b1 = bx << 12 & 0xF000 | ax << 8 & 0x0F00 | cx << 4 & 0x00F0 | dx       & 0xF;
                int b2 = bx       & 0xF000 | ax >> 4 & 0x0F00 | cx >> 8 & 0x00F0 | dx >> 12;
                int b3 = bx <<  4 & 0xF000 | ax      & 0x0F00 | cx >> 4 & 0x00F0 | dx >>  8 & 0xF;

                int dst = dst_line;
                for (int i = 0; i < rows; ++i)
                {
                    for (int j = 0; j < 8; j += 2)
                    {
                        byte px = (byte)((((b0 << j) & 0x80) >> 3)
                                       | (((b1 << j) & 0x80) >> 2)
                                       | (((b2 << j) & 0x80) >> 1)
                                       | (((b3 << j) & 0x80)     ));
                        px |= (byte)((((b0 << j) & 0x40) >> 6)
                                   | (((b1 << j) & 0x40) >> 5)
                                   | (((b2 << j) & 0x40) >> 4)
                                   | (((b3 << j) & 0x40) >> 3));
                        m_output[dst++] = px;
                    }
                    b0 >>= 8;
                    b1 >>= 8;
                    b2 >>= 8;
                    b3 >>= 8;
                }
                dst_line += m_output_stride;
            }
        }

        byte[] m_pixels = new byte[0x100];

        void InitPixels ()
        {
            int dst = m_pixels.Length - 1;
            for (int i = 0x0F; i >= 0; --i)
            {
                byte n = (byte)i;
                for (int j = 0; j < 0x10; ++j)
                {
                    m_pixels[dst--] = (byte)(n-- & 0xF);
                }
            }
        }

        int m_bits;
        int m_bit_count;

        void InitBitReader ()
        {
            m_bits = m_input.ReadUInt16();
            m_bit_count = 16;
        }

        byte GetNextBit ()
        {
            int bit = m_bits & 1;
            m_bits >>= 1;
            if (--m_bit_count <= 0)
            {
                if (m_input.PeekByte() != -1)
                    m_bits = m_input.ReadUInt16();
                else
                    m_bits = 0;
                m_bit_count = 16;
            }
            return (byte)bit;
        }

        BitmapPalette ReadPalette ()
        {
            using (var bits = new MsbBitStream (m_input.AsStream, true))
            {
                var colors = new Color[16];
                for (int i = 0; i < 16; ++i)
                {
                    int r = bits.GetBits (4) * 0x11;
                    int g = bits.GetBits (4) * 0x11;
                    int b = bits.GetBits (4) * 0x11;
                    colors[i] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
                }
                return new BitmapPalette (colors);
            }
        }
    }
}
