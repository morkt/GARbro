//! \file       ImageGDT.cs
//! \date       2023 Sep 29
//! \brief      AGS engine image format (PC-98).
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

namespace GameRes.Formats.CsWare
{
    internal class GdtMetaData : ImageMetaData
    {
        public  byte    Flags;

        public bool HasPalette => (Flags & 0x80) != 0;
        public bool   IsDouble => (Flags & 0x40) == 0;
    }

    [Export(typeof(ImageFormat))]
    public class GdtFormat : ImageFormat
    {
        public override string         Tag => "GDT";
        public override string Description => "AGS engine image format";
        public override uint     Signature => 0x314144; // 'DA1'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (16);
            var info = new GdtMetaData {
                OffsetX = header[8] << 3,
                OffsetY = header.ToUInt16 (0xA),
                Width   = (uint)header[9] << 3,
                Height  = header.ToUInt16 (0xC),
                BPP     = 4,
                Flags   = header[0xF],
            };
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GdtReader (file, (GdtMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GdtFormat.Write not implemented");
        }
    }

    internal class GdtReader
    {
        IBinaryStream   m_input;
        GdtMetaData     m_info;
        int             m_stride;
        int             m_output_stride;

        public BitmapPalette Palette { get; private set; }

        public GdtReader (IBinaryStream file, GdtMetaData info)
        {
            m_input = file;
            m_info = info;
            m_stride = info.iWidth >> 3;
            m_output_stride = info.iWidth >> 1;
        }

        byte[][]    m_planes;

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            if (m_info.HasPalette)
            {
                Palette = ReadPalette();
            }
            var packed_sizes = new ushort[4];
            for (int i = 0; i < 4; ++i)
                packed_sizes[i] = m_input.ReadUInt16();
            long plane_pos = m_input.Position;
            int plane_size = m_stride * m_info.iHeight;
            m_planes = new byte[][] {
                new byte[plane_size], new byte[plane_size], new byte[plane_size], new byte[plane_size],
            };
            Action<int> UnpackPlane = UnpackSingle;
            if (m_info.IsDouble)
                UnpackPlane = UnpackDouble;
            for (int i = 0; i < 4; ++i)
            {
                m_input.Position = plane_pos;
                plane_pos += packed_sizes[i];
                UnpackPlane (i);
            }
            var pixels = new byte[m_output_stride * m_info.iHeight];
            FlattenPlanes (pixels);
            PixelFormat format;
            if (null == Palette)
                format = PixelFormats.Gray4;
            else
                format = PixelFormats.Indexed4;
            return ImageData.Create (m_info, format, Palette, pixels, m_output_stride);
        }

        void UnpackSingle (int plane_index)
        {
            int h = m_info.iHeight;
            int w = m_stride;
            int dst = 0;
            while (w --> 0)
            {
                Unpack8Line (plane_index, dst);
                dst += h;
            }
        }

        void UnpackDouble (int plane_index)
        {
            var output = m_planes[plane_index];
            int h = m_info.iHeight;
            int width = m_stride;
            int dst = 0;
            if ((m_info.OffsetX & 8) != 0)
            {
                Unpack8Line (plane_index, dst);
                --width;
                dst += h;
            }
            if ((m_input.Position & 1) != 0)
                m_input.Seek (1, SeekOrigin.Current);
            if (1 == width)
            {
                Unpack8Line (plane_index, dst);
                return;
            }
            while (m_input.PeekByte() != -1)
            {
                byte op  = m_input.ReadUInt8();
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0x80)
                {
                    if (0 == ctl)
                        continue;
                    ushort w = (ushort)(((op & 0xF) << 8 | (op & 0xF0) >> 4) * 0x11);
                    int count = ctl;
                    Fill (output, dst  , count, w);
                    Fill (output, dst+h, count, w);
                    dst += count * 2;
                }
                else if (ctl < 0xC0)
                {
                    int count = ctl & 0x3F;
                    int w = op & 0xF | (op & 0xF0) << 4;
                    uint d = (uint)(w | (w & 0x0303) << 18 | (w & 0x0C0C) << 14);
                    d = Binary.BigEndian (d | d << 4);
                    Fill (output, dst  , count, d);
                    Fill (output, dst+h, count, d);
                    dst += count * 4;
                }
                else if (ctl < 0xD0)
                {
                    byte b = (byte)((ctl & 0xF) | ctl << 4);
                    int count = op;
                    Fill (output, dst  , count, b);
                    Fill (output, dst+h, count, b);
                    dst += count;
                }
                else if (ctl < 0xD2)
                {
                    int count = (ctl & 1) << 8 | op;
                    while (count --> 0)
                    {
                        output[dst  ] = m_input.ReadUInt8();
                        output[dst+h] = m_input.ReadUInt8();
                    }
                }
                else if (0xD2 == ctl)
                {
                    dst += op;
                }
                else if (ctl < 0xF3)
                {
                    int count = op;
                    int off = 0;
                    switch (ctl)
                    {
                    case 0xD3: off = 16; break;
                    case 0xD4: off = 12; break;
                    case 0xD5: off = 8; break;
                    case 0xD6: off = 4; break;
                    case 0xD7: off = 2; break;
                    case 0xD8: off = 1; break;
                    case 0xD9: off = h * 2 + 8; break;
                    case 0xDA: off = h * 2 + 4; break;
                    case 0xDB: off = h * 2 + 2; break;
                    case 0xDC: off = h * 2 + 1; break;
                    case 0xDD: off = h * 2; break;
                    case 0xDE: off = h * 2 - 1; break;
                    case 0xDF: off = h * 2 - 2; break;
                    case 0xE0: off = h * 2 - 4; break;
                    case 0xE1: off = h * 2 - 8; break;
                    case 0xE2: off = h * 4 + 8; break;
                    case 0xE3: off = h * 4 + 4; break;
                    case 0xE4: off = h * 4 + 2; break;
                    case 0xE5: off = h * 4 + 1; break;
                    case 0xE6: off = h * 4; break;
                    case 0xE7: off = h * 4 - 1; break;
                    case 0xE8: off = h * 4 - 2; break;
                    case 0xE9: off = h * 4 - 4; break;
                    case 0xEA: off = h * 4 - 8; break;
                    case 0xEB: off = h * 6 + 4; break;
                    case 0xEC: off = h * 6 + 2; break;
                    case 0xED: off = h * 6 + 1; break;
                    case 0xEE: off = h * 6; break;
                    case 0xEF: off = h * 6 - 1; break;
                    case 0xF0: off = h * 6 - 2; break;
                    case 0xF1: off = h * 6 - 4; break;
                    case 0xF2: off = h * 8; break;
                    }
                    Binary.CopyOverlapped (output, dst-off, dst, count);
                    Binary.CopyOverlapped (output, dst-off+h, dst+h, count);
                    dst += count;
                }
                else if (ctl < 0xFC)
                {
                    int count = op;
                    var source = m_planes[(ctl - 0xF3) % 3];
                    if (ctl < 0xF6)
                    {
                        Buffer.BlockCopy (source, dst, output, dst, count);
                        Buffer.BlockCopy (source, dst+h, output, dst+h, count);
                        dst += count;
                    }
                    else if (ctl > 0xF8)
                    {
                        var source1 = m_planes[ctl & 1];
                        var source2 = m_planes[ctl & 2];
                        while (count --> 0)
                        {
                            output[dst] = (byte)(source1[dst] & source2[dst]);
                            output[dst+h] = (byte)(source1[dst+h] & source2[dst+h]);
                            ++dst;
                        }
                    }
                    else
                    {
                        while (count --> 0)
                        {
                            output[dst] = (byte)~source[dst];
                            output[dst+h] = (byte)~source[dst+h];
                            ++dst;
                        }
                    }
                }
                else if (0xFC == ctl)
                {
                    int count = op;
                    byte b = m_input.ReadUInt8();
                    Fill (output, dst  , count, b);
                    Fill (output, dst+h, count, b);
                    dst += count;
                }
                else if (0xFD == ctl)
                {
                    if (op < 0x80)
                    {
                        int count = op;
                        ushort w = m_input.ReadUInt16();
                        Fill (output, dst  , count, w);
                        Fill (output, dst+h, count, w);
                        dst += count * 2;
                    }
                    else
                    {
                        int count = op & 0x7F;
                        ushort w1 = m_input.ReadUInt16();
                        ushort w2 = m_input.ReadUInt16();
                        Fill (output, dst  , count, (ushort)(w1 << 8   | w2 & 0xFF));
                        Fill (output, dst+h, count, (ushort)(w1 & 0xFF | w2 >> 8));
                        dst += count * 2;
                    }
                }
                else if (0xFE == ctl)
                {
                    int count = op & 0x3F;
                    if (op < 0x80)
                    {
                        byte b0 = m_input.ReadUInt8();
                        byte b1 = m_input.ReadUInt8();
                        int d = b1 | b0 << 16;
                        d = d & 0x0F000F | (d & 0xF000F0) << 4;
                        d *= 0x11;
                        d = Binary.BigEndian (d);
                        Fill (output, dst  , count, (uint)d);
                        Fill (output, dst+h, count, (uint)d);
                    }
                    else
                    {
                        uint d0 = m_input.ReadUInt32();
                        uint d1 = m_input.ReadUInt32();
                        uint p0 = d0 << 24 | d0 & 0xFF0000 | (d1 & 0xFF) << 8 | (d1 & 0xFF0000) >> 16;
                        uint p1 = (d0 & 0xFF00) << 16 | (d0 & 0xFF000000) >> 8 | d1 & 0xFF00 | (d1 & 0xFF000000) >> 24;
                        Fill (output, dst  , count, p0);
                        Fill (output, dst+h, count, p1);
                    }
                    dst += count * 4;
                }
                else // 0xFF
                {
                    dst += h;
                    width -= 2;
                    if (0 == width)
                        break;
                    if (1 == width)
                    {
                        Unpack8Line (plane_index, dst);
                        break;
                    }
                }
            }
        }

        void Unpack8Line (int plane_index, int dst)
        {
            var output = m_planes[plane_index];
            int h = m_info.iHeight;
            int end_pos = dst + h;
            while (m_input.PeekByte() != -1)
            {
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0x40)
                {
                    byte b = 0;
                    if (ctl >= 0x20)
                        b = 0xFF;
                    int count = ctl & 0x1F;
                    if (0 == count)
                        count = m_input.ReadUInt8();
                    Fill (output, dst, count, b);
                    dst += count;
                }
                else if (ctl < 0xA0)
                {
                    int count = ctl & 0x1F;
                    if (0 == count)
                        count = m_input.ReadUInt8();
                    int src_plane = (ctl - 0x40) >> 5;
                    Buffer.BlockCopy (m_planes[src_plane], dst, output, dst, count);
                    dst += count;
                }
                else if (ctl < 0xF0)
                {
                    int count = ctl & 0xF;
                    if (0 == count)
                        count = m_input.ReadUInt8();
                    switch (ctl & 0xF0)
                    {
                    case 0xA0: Binary.CopyOverlapped (output, dst-16, dst, count); break;
                    case 0xB0: Binary.CopyOverlapped (output, dst-8, dst, count); break;
                    case 0xC0: Binary.CopyOverlapped (output, dst-4, dst, count); break;
                    case 0xD0: Binary.CopyOverlapped (output, dst-2, dst, count); break;
                    case 0xE0: Binary.CopyOverlapped (output, dst-h*2, dst, count); break;
                    }
                    dst += count;
                }
                else if (ctl < 0xF9)
                {
                    int count = ctl & 0xF;
                    if (0 == count)
                        count = m_input.ReadUInt8();
                    m_input.Read (output, dst, count);
                    dst += count;
                }
                else if (0xF9 == ctl)
                {
                    dst += m_input.ReadUInt8();
                }
                else if (0xFA == ctl)
                {
                    int count = m_input.ReadUInt8();
                    byte b = m_input.ReadUInt8();
                    Fill (output, dst, count, b);
                    dst += count;
                }
                else if (0xFB == ctl)
                {
                    int count = m_input.ReadUInt8();
                    int b = count >> 7;
                    count &= 0x7F;
                    while (count --> 0)
                    {
                        output[dst] = (byte)~m_planes[b][dst];
                        ++dst;
                    }
                }
                else if (0xFC == ctl)
                {
                    int count = m_input.ReadUInt8();
                    if ((count & 0x80) != 0)
                    {
                        count &= 0x7F;
                        byte b = m_input.ReadUInt8();
                        ushort d = (ushort)(b & 0xF | (b & 0xF0) << 4);
                        d |= (ushort)(d << 4);
                        Fill (output, dst, count, d);
                        dst += count * 2;
                    }
                    else
                    {
                        while (count --> 0)
                        {
                            output[dst] = (byte)~m_planes[2][dst];
                            ++dst;
                        }
                    }
                }
                else if (0xFD == ctl)
                {
                    int count = m_input.ReadUInt8();
                    if ((count & 0x80) != 0)
                    {
                        byte b = m_input.ReadUInt8();
                        uint d = (uint)(b & 0xF | b << 4 | (b & 0xF0) << 8);
                        if ((count & 0x40) != 0)
                        {
                            b = m_input.ReadUInt8();
                            d |= (uint)((b & 0xF) << 16 | b << 20 | (b & 0xF0) << 24);
                        }
                        else
                        {
                            d |= (d & 0x3F3F) << 18 | (d & 0xC0C0) << 10;
                        }
                        count &= 0x3F;
                        Fill (output, dst, count, d);
                        dst += count * 4;
                    }
                    else
                    {
                        ushort w = m_input.ReadUInt16();
                        Fill (output, dst, count, w);
                        dst += count * 2;
                    }
                }
                else if (0xFE == ctl)
                {
                    int count = m_input.ReadUInt8();
                    int b = count & 0xC0;
                    if (b != 0)
                    {
                        count &= 0x3F;
                        b >>= 6;
                        while (count --> 0)
                        {
                            output[dst] = (byte)(m_planes[b & 1][dst] & m_planes[b & 2][dst]);
                            ++dst;
                        }
                    }
                    else
                    {
                        uint u = m_input.ReadUInt32();
                        Fill (output, dst, count, u);
                        dst += count * 4;
                    }
                }
                else // 0xFF
                {
                    break;
                }
            }
        }

        void FlattenPlanes (byte[] output)
        {
            int plane_size = m_planes[0].Length;
            int src = 0;
            for (int x = 0; x < m_output_stride; x += 4)
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
                    dst += m_output_stride;
                }
            }
        }

        static void Fill (byte[] output, int dst, int count, byte pixel)
        {
            while (count --> 0)
            {
                output[dst++] = pixel;
            }
        }

        static void Fill (byte[] output, int dst, int count, ushort pixel)
        {
            count <<= 1;
            for (int i = 0; i < count; i += 2)
            {
                LittleEndian.Pack (pixel, output, dst+i);
            }
        }

        static void Fill (byte[] output, int dst, int count, uint pixel)
        {
            count <<= 2;
            for (int i = 0; i < count; i += 4)
            {
                LittleEndian.Pack (pixel, output, dst+i);
            }
        }

        BitmapPalette ReadPalette ()
        {
            using (var bits = new MsbBitStream (m_input.AsStream, true))
            {
                var colors = new Color[16];
                for (int i = 0; i < 16; ++i)
                {
                    int b = bits.GetBits (4) * 0x11;
                    int r = bits.GetBits (4) * 0x11;
                    int g = bits.GetBits (4) * 0x11;
                    colors[i] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
                }
                return new BitmapPalette (colors);
            }
        }
    }
}
