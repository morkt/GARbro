//! \file       ImageMAI2.cs
//! \date       2023 Oct 24
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Izumi
{
    internal class Mai2MetaData : ImageMetaData
    {
        public byte     Flags;
        public ushort   Plane0Size;
        public ushort   Plane1Size;
        public ushort   Plane2Size;
        public ushort   Plane3Size;

        public bool HasPalette => (Flags & 0x80) != 0;
    }

    [Export(typeof(ImageFormat))]
    public class Mai2Format : ImageFormat
    {
        public override string         Tag => "MAI/IZUMI";
        public override string Description => "Izumi engine image format";
        public override uint     Signature => 0x3249414D; // 'MAI2'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            ushort xy = header.ToUInt16 (4);
            return new Mai2MetaData {
                Width  = (uint)(header.ToUInt16 (6) << 3),
                Height = header.ToUInt16 (8),
                OffsetX = xy % 0x50,
                OffsetY = xy / 0x50,
                BPP = 4,
                Flags = header[0xA],
                Plane0Size = header.ToUInt16 (0x0C),
                Plane1Size = header.ToUInt16 (0x0E),
                Plane2Size = header.ToUInt16 (0x10),
                Plane3Size = header.ToUInt16 (0x12),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Mai2Reader (file, (Mai2MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mai2Format.Write not implemented");
        }
    }

    internal class Mai2Reader
    {
        IBinaryStream   m_input;
        Mai2MetaData    m_info;

        public Mai2Reader (IBinaryStream input, Mai2MetaData info)
        {
            m_input = input;
            m_info = info;
        }

        byte[][] m_planes;
        int m_stride;
        int m_height;

        public ImageData Unpack ()
        {
            m_input.Position = 0x14;
            BitmapPalette palette = null;
            if (m_info.HasPalette)
                palette = ReadPalette();
            else
                palette = BitmapPalettes.Gray16;

            m_height = m_info.iHeight;
            m_stride = m_info.iWidth >> 3;
            int plane_size = m_stride * m_info.iHeight;
            m_planes = new byte[][] {
                new byte[plane_size], new byte[plane_size], new byte[plane_size], new byte[plane_size],
            };

            long next_pos = m_input.Position + m_info.Plane0Size;
            if ((m_info.Flags & 1) != 0)
                UnpackPlane (m_planes[0]);

            m_input.Position = next_pos;
            next_pos += m_info.Plane1Size;
            if ((m_info.Flags & 2) != 0)
                UnpackPlane (m_planes[1]);

            m_input.Position = next_pos;
            next_pos += m_info.Plane2Size;
            if ((m_info.Flags & 4) != 0)
                UnpackPlane (m_planes[2]);

            m_input.Position = next_pos;
            if ((m_info.Flags & 8) != 0)
                UnpackPlane (m_planes[3]);

            int output_stride = m_info.iWidth >> 1;
            var output = new byte[output_stride * m_info.iHeight];
            FlattenPlanes (output, output_stride);

            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, output, output_stride);
        }

        void UnpackPlane (byte[] plane)
        {
            int dst_row = 0;
            for (int x = 0; x < m_stride; ++x)
            {
                int dst = dst_row;
                int remaining = m_height;
                while (remaining > 0)
                {
                    int count = 1;
                    int ctl = m_input.ReadUInt8();
                    if (ctl < 0x90)
                    {
                        count = ctl & 0x1F;
                        if (0 == count)
                            count = m_input.ReadUInt8();
                        switch (ctl >> 5)
                        {
                        case 0:
                            for (int i = 0; i < count; ++i)
                                plane[dst++] = 0;
                            break;
                        case 1:
                            for (int i = 0; i < count; ++i)
                                plane[dst++] = 0xFF;
                            break;
                        case 2:
                            Buffer.BlockCopy (m_planes[0], dst, plane, dst, count);
                            dst += count;
                            break;
                        case 3:
                            Buffer.BlockCopy (m_planes[1], dst, plane, dst, count);
                            dst += count;
                            break;
                        case 4:
                            Buffer.BlockCopy (m_planes[2], dst, plane, dst, count);
                            dst += count;
                            break;
                        }
                    }
                    else if (ctl < 0xF0)
                    {
                        count = ctl & 0xF;
                        if (0 == count)
                            count = m_input.ReadUInt8();
                        int off = 0;
                        switch (ctl >> 4)
                        {
                        case 0x9: off = 0x10; break;
                        case 0xA: off = 8; break;
                        case 0xB: off = 4; break;
                        case 0xC: off = 2; break;
                        case 0xD: off = m_height << 1; break;
                        case 0xE: off = m_height; break;
                        }
                        Binary.CopyOverlapped (plane, dst - off, dst, count);
                        dst += count;
                    }
                    else if (ctl < 0xF9)
                    {
                        count = ctl & 0xF;
                        if (0 == count)
                            count = m_input.ReadUInt8();
                        m_input.Read (plane, dst, count);
                        dst += count;
                    }
                    else
                    {
                        count = m_input.ReadUInt8();
                        switch (ctl)
                        {
                        case 0xF9:
                            dst += count;
                            break;
                        case 0xFA:
                            {
                                byte b = m_input.ReadUInt8();
                                for (int i = 0; i < count; ++i)
                                    plane[dst++] = b;
                                break;
                            }
                        case 0xFB:
                            {
                                int src = 0;
                                if ((count & 0x80) != 0)
                                {
                                    count &= 0x7F;
                                    src = 1;
                                }
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst] = (byte)~m_planes[src][dst];
                                    dst++;
                                }
                                break;
                            }
                        case 0xFC:
                            if ((count & 0x80) != 0)
                            {
                                count &= 0x7F;
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                byte al, ah;
                                al = m_input.ReadUInt8();
                                ah = (byte)(al << 4 | al & 0x0F);
                                al = (byte)(al >> 4 | al & 0xF0);
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst++] = al;
                                    plane[dst++] = ah;
                                }
                                count <<= 1;
                            }
                            else
                            {
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst] = (byte)~m_planes[2][dst];
                                    dst++;
                                }
                            }
                            break;
                        case 0xFD:
                            if ((count & 0x80) != 0)
                            {
                                ctl = count;
                                count = ctl & 0x3F;
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                byte al, ah, bl, bh;
                                al = m_input.ReadUInt8();
                                bl = (byte)(al & 0xF0 | al >> 4);
                                bh = (byte)(al & 0x0F | al << 4);
                                if (ctl < 0xC0)
                                {
                                    al = Binary.RotByteR (bl, 2);
                                    ah = Binary.RotByteR (bh, 2);
                                }
                                else
                                {
                                    ah = m_input.ReadUInt8();
                                    al = (byte)(ah & 0xF0 | ah >> 4);
                                    ah = (byte)(ah & 0x0F | ah << 4);
                                }
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst++] = bl;
                                    plane[dst++] = bh;
                                    plane[dst++] = al;
                                    plane[dst++] = ah;
                                }
                                count <<= 2;
                            }
                            else
                            {
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                byte al = m_input.ReadUInt8();
                                byte ah = m_input.ReadUInt8();
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst++] = al;
                                    plane[dst++] = ah;
                                }
                                count <<= 1;
                            }
                            break;
                        case 0xFE:
                            ctl = count;
                            count &= 0x3F; 
                            if (0 == count)
                                count = m_input.ReadUInt8();
                            if (ctl < 0x40)
                            {
                                m_input.Read (plane, dst, 4);
                                count <<= 2;
                                Binary.CopyOverlapped (plane, dst, dst + 4, count - 4);
                                dst += count;
                            }
                            else
                            {
                                int psrc, pmask;
                                if ((ctl & 0x80) == 0)
                                {
                                    psrc = 0;
                                    pmask = 1;
                                }
                                else if (ctl < 0xC0)
                                {
                                    psrc = 0;
                                    pmask = 2;
                                }
                                else
                                {
                                    psrc = 1;
                                    pmask = 2;
                                }
                                for (int i = 0; i < count; ++i)
                                {
                                    byte b = m_planes[psrc][dst];
                                    b &= m_planes[pmask][dst];
                                    plane[dst++] = b;
                                }
                            }
                            break;
                        case 0xFF:
                            {
                                Func<int, byte> op;
                                if (count < 0x40)
                                {
                                    op = src => (byte)(m_planes[0][src] | m_planes[1][src]);
                                }
                                else if (count < 0x80)
                                {
                                    op = src => (byte)(m_planes[0][src] ^ m_planes[1][src]);
                                    count &= 0x3F;
                                }
                                else
                                {
                                    if (count < 0xA0)
                                        op = src => (byte)(m_planes[0][src] | m_planes[2][src]);
                                    else if (count < 0xC0)
                                        op = src => (byte)(m_planes[1][src] | m_planes[2][src]);
                                    else if (count < 0xE0)
                                        op = src => (byte)(m_planes[0][src] ^ m_planes[2][src]);
                                    else
                                        op = src => (byte)(m_planes[1][src] ^ m_planes[2][src]);
                                    count &= 0x1F;
                                }
                                if (0 == count)
                                    count = m_input.ReadUInt8();
                                for (int i = 0; i < count; ++i)
                                {
                                    plane[dst] = op (dst);
                                    dst++;
                                }
                                break;
                            }
                        }
                    }
                    remaining -= count;
                }
                dst_row += m_height;
            }
        }

        void FlattenPlanes (byte[] output, int output_stride)
        {
            int plane_size = m_planes[0].Length;
            int src = 0;
            for (int x = 0; x < output_stride; x += 4)
            {
                int dst = x;
                for (int y = 0; y < m_info.iHeight; ++y)
                {
                    byte b0 = m_planes[0][src];
                    byte b1 = m_planes[1][src];
                    byte b2 = m_planes[2][src];
                    byte b3 = m_planes[3][src];
                    ++src;
                    for (int j = 0; j < 8; j += 2)
                    {
                        byte px = (byte)((((b0 << j) & 0x80) >> 3)
                                       | (((b1 << j) & 0x80) >> 2)
                                       | (((b2 << j) & 0x80) >> 1)
                                       | (((b3 << j) & 0x80) >> 0));
                        px |= (byte)((((b0 << j) & 0x40) >> 6)
                                   | (((b1 << j) & 0x40) >> 5)
                                   | (((b2 << j) & 0x40) >> 4)
                                   | (((b3 << j) & 0x40) >> 3));
                        output[dst+j/2] = px;
                    }
                    dst += output_stride;
                }
            }
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
