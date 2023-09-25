//! \file       ImagePL4.cs
//! \date       2023 Sep 23
//! \brief      Pearl Soft image format.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [980424][Pearl Soft] Watashi

namespace GameRes.Formats.Pearl
{
    internal class Pl4MetaData : ImageMetaData
    {
        public ushort CompressionMethod;
    }

    [Export(typeof(ImageFormat))]
    public class Pl4Format : ImageFormat
    {
        public override string         Tag => "PL4";
        public override string Description => "Pearl Soft image format";
        public override uint     Signature => 0x20344C50; // 'PL4 '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            int version = header.ToUInt16 (4);
            if (version != 1)
                return null;
            var info = new Pl4MetaData {
                Width  = header.ToUInt16 (0xC) * 8u,
                Height = header.ToUInt16 (0xE),
                CompressionMethod = header.ToUInt16 (6),
                BPP = 8,
            };
            if (info.CompressionMethod > 1)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Pl4Reader (file, (Pl4MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pl4Format.Write not implemented");
        }
    }

    internal class Pl4Reader
    {
        IBinaryStream   m_input;
        Pl4MetaData     m_info;
        int             m_stride;

        public Pl4Reader (IBinaryStream input, Pl4MetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = m_info.iWidth;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            var palette = ReadPalette (16);
            var pixels = new byte[m_stride * m_info.iHeight];
            m_input.Position = 0x40;
            if (m_info.CompressionMethod == 0)
                UnpackV0 (pixels);
            else if (m_info.CompressionMethod == 1)
                UnpackV1 (pixels);
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, pixels, m_stride);
        }

        BitmapPalette ReadPalette (int colors)
        {
            var color_data = m_input.ReadBytes (colors * 3);
            var color_map = new Color[colors];
            int src = 0;
            for (int i = 0; i < colors; ++i)
            {
                color_map[i] = Color.FromRgb ((byte)(color_data[src  ] * 0x11),
                                              (byte)(color_data[src+1] * 0x11),
                                              (byte)(color_data[src+2] * 0x11));
                src += 3;
            }
            return new BitmapPalette (color_map);
        }

        void UnpackV0 (byte[] output)
        {
            int height = m_info.iHeight;
            int x = m_info.iWidth / 4;
            int output_size = height * m_stride;
            int row = 0;
            int dst = 0;
            int ctl, word;
            while ((ctl = m_input.ReadByte()) != -1)
            {
                byte next = m_input.ReadUInt8();
                if (0x98 == ctl)
                {
                    ctl = m_input.ReadUInt8();
                    if (0 == next)
                    {
                        next = m_input.ReadUInt8();
                    }
                    else
                    {
                        word = ctl << 8 | next;
                        int count = ((word >> 1) & 0x1F) + 2;
                        int src_y;
                        int src_x = Math.DivRem ((word >> 6) + 1, height, out src_y);
                        int src = dst - src_y * m_stride - 4 * src_x;
                        src_y = row - src_y;
                        if (src_y < 0)
                        {
                            src_y += height;
                            src += output_size - 4;
                        }
                        while (count --> 0)
                        {
                            output[dst  ] = output[src  ];
                            output[dst+1] = output[src+1];
                            output[dst+2] = output[src+2];
                            output[dst+3] = output[src+3];
                            dst += m_stride;
                            if (++row >= height)
                            {
                                row = 0;
                                dst -= output_size - 4;
                                if (--x <= 0)
                                    return;
                            }
                            src += m_stride;
                            if (++src_y >= height)
                            {
                                src_y = 0;
                                src -= output_size - 4;
                            }
                        }
                        continue;
                    }
                }
                word = next << 8 | ctl;
                int px = 0;
                if ((word & 0x1000) != 0) px  = 0x01000000;
                if ((word & 0x2000) != 0) px |= 0x00010000;
                if ((word & 0x4000) != 0) px |= 0x00000100;
                if ((word & 0x8000) != 0) px |= 0x00000001;
                if ((word & 0x0100) != 0) px |= 0x02000000;
                if ((word & 0x0200) != 0) px |= 0x00020000;
                if ((word & 0x0400) != 0) px |= 0x00000200;
                if ((word & 0x0800) != 0) px |= 0x00000002;
                if ((word & 0x0010) != 0) px |= 0x04000000;
                if ((word & 0x0020) != 0) px |= 0x00040000;
                if ((word & 0x0040) != 0) px |= 0x00000400;
                if ((word & 0x0080) != 0) px |= 0x00000004;
                if ((word & 0x0001) != 0) px |= 0x08000000;
                if ((word & 0x0002) != 0) px |= 0x00080000;
                if ((word & 0x0004) != 0) px |= 0x00000800;
                if ((word & 0x0008) != 0) px |= 0x00000008;
                LittleEndian.Pack (px, output, dst);
                dst += m_stride;
                if (++row >= height)
                {
                    row = 0;
                    dst -= output_size - 4;
                    if (--x <= 0)
                        break;
                }
            }
        }

        byte[]          m_pixelBuffer;
        MsbBitStream    m_bits;

        void UnpackV1 (byte[] output)
        {
            m_pixelBuffer = InitLineBuffer();
            int height = m_info.iHeight;
            int dst = 0;
            int output_size = m_stride * height;
            int x = m_info.iWidth / 8;
            int y = 0;
            using (m_bits = new MsbBitStream (m_input.AsStream, true))
            {
                int p1 = 0, p2 = 0, p3 = 0, p4 = 0;
                int ctl_bit;
                while ((ctl_bit = m_bits.GetNextBit()) != -1)
                {
                    if (ctl_bit != 0)
                    {
                        int src = dst;
                        int src_y = y;
                        switch (m_bits.GetBits (2))
                        {
                        case 0:
                            src_y = y - 2;
                            src = dst - 2 * m_stride;
                            break;
                        case 1:
                            src_y = y - 1;
                            src = dst - m_stride;
                            break;
                        case 2:
                            src_y = y - 4;
                            src = dst - 4 * m_stride;
                            break;
                        case 3:
                            src = dst - 8;
                            break;
                        }
                        if (src_y < 0)
                        {
                            src_y += height;
                            src += output_size - 8;
                        }
                        int count_length = 0;
                        while (m_bits.GetNextBit() == 0)
                            ++count_length;
                        int count = 1;
                        if (count_length != 0)
                        {
                            count = m_bits.GetBits (count_length) | 1 << count_length;
                        }
                        while (count --> 0)
                        {
                            Buffer.BlockCopy (output, src, output, dst, 8);
                            dst += m_stride;
                            if (++y >= height)
                            {
                                y = 0;
                                dst -= output_size - 8;
                                if (--x <= 0)
                                    return;
                            }
                            src += m_stride;
                            if (++src_y >= height)
                            {
                                src_y = 0;
                                src -= output_size - 8;
                            }
                        }
                    }
                    else
                    {
                        int px1 = 0;
                        int px2 = 0;
                        p1 = UpdatePixel (p1);
                        p2 = UpdatePixel (p2);
                        p3 = UpdatePixel (p3);
                        p4 = UpdatePixel (p4);
                        if ((p1 & 0x80) != 0) px1  = 0x00000001;
                        if ((p1 & 0x40) != 0) px1 |= 0x00000100;
                        if ((p1 & 0x20) != 0) px1 |= 0x00010000;
                        if ((p1 & 0x10) != 0) px1 |= 0x01000000;
                        if ((p1 & 0x08) != 0) px2  = 0x00000001;
                        if ((p1 & 0x04) != 0) px2 |= 0x00000100;
                        if ((p1 & 0x02) != 0) px2 |= 0x00010000;
                        if ((p1 & 0x01) != 0) px2 |= 0x01000000;
                        if ((p2 & 0x80) != 0) px1 |= 0x00000002;
                        if ((p2 & 0x40) != 0) px1 |= 0x00000200;
                        if ((p2 & 0x20) != 0) px1 |= 0x00020000;
                        if ((p2 & 0x10) != 0) px1 |= 0x02000000;
                        if ((p2 & 0x08) != 0) px2 |= 0x00000002;
                        if ((p2 & 0x04) != 0) px2 |= 0x00000200;
                        if ((p2 & 0x02) != 0) px2 |= 0x00020000;
                        if ((p2 & 0x01) != 0) px2 |= 0x02000000;
                        if ((p3 & 0x80) != 0) px1 |= 0x00000004;
                        if ((p3 & 0x40) != 0) px1 |= 0x00000400;
                        if ((p3 & 0x20) != 0) px1 |= 0x00040000;
                        if ((p3 & 0x10) != 0) px1 |= 0x04000000;
                        if ((p3 & 0x08) != 0) px2 |= 0x00000004;
                        if ((p3 & 0x04) != 0) px2 |= 0x00000400;
                        if ((p3 & 0x02) != 0) px2 |= 0x00040000;
                        if ((p3 & 0x01) != 0) px2 |= 0x04000000;
                        if ((p4 & 0x80) != 0) px1 |= 0x00000008;
                        if ((p4 & 0x40) != 0) px1 |= 0x00000800;
                        if ((p4 & 0x20) != 0) px1 |= 0x00080000;
                        if ((p4 & 0x10) != 0) px1 |= 0x08000000;
                        if ((p4 & 0x08) != 0) px2 |= 0x00000008;
                        if ((p4 & 0x04) != 0) px2 |= 0x00000800;
                        if ((p4 & 0x02) != 0) px2 |= 0x00080000;
                        if ((p4 & 0x01) != 0) px2 |= 0x08000000;
                        LittleEndian.Pack (px1, output, dst);
                        LittleEndian.Pack (px2, output, dst+4);
                        dst += m_stride;
                        if (++y >= height)
                        {
                            y = 0;
                            dst -= output_size - 8;
                            if (--x <= 0)
                                break;
                        }
                    }
                }
            }
        }

        int UpdatePixel (int pixel)
        {
            byte nibble = GetNextPixel (pixel);
            return GetNextPixel (nibble) | nibble << 4;
        }

        byte GetNextPixel (int pixel)
        {
            int bits = GetPixelBits();
            int prior = (pixel & 0xF) << 4;
            byte next = m_pixelBuffer[prior+bits];
            int pos = prior + bits;
            if (bits == 0)
                return next;
            while (bits --> 0)
            {
                m_pixelBuffer[pos] = m_pixelBuffer[pos - 1];
                --pos;
            }
            return m_pixelBuffer[prior] = next;
        }

        int GetPixelBits ()
        {
            if (m_bits.GetNextBit() != 0)
            {
                return m_bits.GetBits (1);
            }
            else if (m_bits.GetNextBit() != 0)
            {
                return m_bits.GetBits (1) + 2;
            }
            else if (m_bits.GetNextBit() != 0)
            {
                return m_bits.GetBits (2) + 4;
            }
            else
            {
                return m_bits.GetBits (3) + 8;
            }
        }

        static byte[] InitLineBuffer ()
        {
            var buffer = new byte[256];
            for (int i = 0; i < 256; ++i)
            {
                buffer[i] = (byte)((i + (i >> 4)) & 0xF);
            }
            return buffer;
        }
    }
}
