//! \file       ImagePR1.cs
//! \date       2023 Oct 04
//! \brief      Discovery image format (PC-98).
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

namespace GameRes.Formats.Discovery
{
    internal class PrMetaData : ImageMetaData
    {
        public byte     Flags;
        public byte     Mask;

        public bool IsLeftToRight => (Flags & 1) != 0;
    }

    [Export(typeof(ImageFormat))]
    public class Pr1Format : ImageFormat
    {
        public override string         Tag => "PR1";
        public override string Description => "Discovery image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasAnyOfExtensions (".PR1", ".AN1"))
                return null;
            var header = file.ReadHeader (12);
            return new PrMetaData {
                Width = (uint)header.ToUInt16 (8) << 3,
                Height = header.ToUInt16 (0xA),
                OffsetX = header.ToUInt16 (2),
                OffsetY = header.ToUInt16 (4),
                Flags = header[0],
                Mask = header[1],
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PrReader (file, (PrMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pr1Format.Write not implemented");
        }
    }

    internal class PrReader
    {
        IBinaryStream   m_input;
        PrMetaData      m_info;

        Action      IncrementDest;
        Func<bool>  IsDone;

        public PrMetaData Info => m_info;

        public PrReader (IBinaryStream file, PrMetaData info)
        {
            m_input = file;
            m_info = info;
            if (m_info.IsLeftToRight)
            {
                IncrementDest = IncLeftToRight;
                IsDone = () => m_dst >= m_plane_size;
            }
            else
            {
                IncrementDest = IncTopToBottom;
                IsDone = () => m_x >= m_stride;
            }
        }

        protected BitmapPalette m_palette;
        protected int       m_stride;
        protected int       m_plane_size;
        protected byte[][]  m_planes;
        int m_dst;
        int m_x;

        protected void UnpackPlanes ()
        {
            const int buffer_slice = 0x410;
            m_input.Position = 0xC;
            m_palette = ReadPalette();
            m_stride = m_info.iWidth >> 3;
            m_plane_size = m_stride * m_info.iHeight;
            m_planes = new byte[][] {
                new byte[m_plane_size], new byte[m_plane_size], new byte[m_plane_size], new byte[m_plane_size],
            };
            var buffer = new byte[buffer_slice * 4];
            var buf_count = new byte[4];
            var offsets = new int[] { 0, buffer_slice, buffer_slice*2, buffer_slice*3 };
            m_dst = 0;
            m_x = 0;
            while (!IsDone())
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                int count = (ctl & 0x1F) + 1;
                bool bit = (ctl & 0x20) != 0;
                ctl >>= 6;
                if (!bit)
                {
                    if (ctl != 0)
                    {
                        int src_pos = ctl;
                        int src_count2 = 1 << (ctl - 1);
                        int pos = offsets[ctl];
                        int count2 = src_count2;
                        do
                        {
                            byte p0 = m_input.ReadUInt8();
                            byte p1 = m_input.ReadUInt8();
                            byte p2 = m_input.ReadUInt8();
                            byte p3 = m_input.ReadUInt8();
                            PutPixels (p0, p1, p2, p3);
                            buffer[pos++] = p0;
                            buffer[pos++] = p1;
                            buffer[pos++] = p2;
                            buffer[pos++] = p3;
                        }
                        while (--count > 0 && --count2 > 0);
                        while (count > 0)
                        {
                            int si = offsets[src_pos];
                            for (int i = 0; i < src_count2; ++i)
                            {
                                byte p0 = buffer[si++];
                                byte p1 = buffer[si++];
                                byte p2 = buffer[si++];
                                byte p3 = buffer[si++];
                                PutPixels (p0, p1, p2, p3);
                                if (--count <= 0)
                                    break;
                            }
                        }
                        offsets[src_pos] += src_count2 * 4;
                        buf_count[src_pos] += (byte)src_count2;
                        if (buf_count[src_pos] == 0)
                            offsets[src_pos] = src_pos * buffer_slice;
                    }
                    else
                    {
                        while (count --> 0)
                        {
                            byte p0 = m_input.ReadUInt8();
                            byte p1 = m_input.ReadUInt8();
                            byte p2 = m_input.ReadUInt8();
                            byte p3 = m_input.ReadUInt8();
                            PutPixels (p0, p1, p2, p3);
                            int pos = offsets[0];
                            buffer[pos++] = p0;
                            buffer[pos++] = p1;
                            buffer[pos++] = p2;
                            buffer[pos++] = p3;
                            offsets[0] += 4;
                            buf_count[0]++;
                            if (0 == buf_count[0])
                                offsets[0] = 0;
                        }
                    }
                }
                else if (ctl != 0)
                {
                    int count2 = 1 << (ctl - 1);
                    int off_diff = count2 << 2;
                    int off_mask = off_diff - 1;
                    int off = m_input.ReadUInt8() << 2;;
                    int base_pos = ctl * buffer_slice;
                    off += base_pos;
                    int src = off;
                    while (count > 0)
                    {
                        off = src;
                        for (int i = 0; i < count2; ++i)
                        {
                            byte p0 = buffer[off];
                            byte p1 = buffer[off+1];
                            byte p2 = buffer[off+2];
                            byte p3 = buffer[off+3];
                            PutPixels (p0, p1, p2, p3);
                            off += 4;
                            int pos = off - base_pos;
                            if ((pos & off_mask) == 0)
                                off -= off_diff;
                            if (--count <= 0)
                                break;
                        }
                    }
                }
                else
                {
                    while (count --> 0)
                    {
                        int off = m_input.ReadUInt8() << 2;
                        byte p0 = buffer[off];
                        byte p1 = buffer[off+1];
                        byte p2 = buffer[off+2];
                        byte p3 = buffer[off+3];
                        PutPixels (p0, p1, p2, p3);
                    }
                }
            }
        }

        public ImageData Unpack ()
        {
            UnpackPlanes();
            int output_stride = m_info.iWidth >> 1;
            var output = new byte[output_stride * m_info.iHeight];
            FlattenPlanes (0, output);
            return ImageData.Create (m_info, PixelFormats.Indexed4, m_palette, output, output_stride);
        }

        void PutPixels (byte p0, byte p1, byte p2, byte p3)
        {
            if (0xFF == m_info.Mask || true) // we don't do overlaying here, just single image decoding
            {
                m_planes[0][m_dst] = p0;
                m_planes[1][m_dst] = p1;
                m_planes[2][m_dst] = p2;
                m_planes[3][m_dst] = p3;
            }
            else
            {
                byte v = m_info.Mask;
                byte mask = p0;
                if ((v & 1) != 0)
                    mask = (byte)~mask;
                if ((v & 2) != 0)
                    mask |= (byte)~p1;
                else
                    mask |= p1;
                if ((v & 4) != 0)
                    mask |= (byte)~p2;
                else
                    mask |= p2;
                if ((v & 8) != 0)
                    mask |= (byte)~p3;
                else
                    mask |= p3;
                p0 &= mask;
                p1 &= mask;
                p2 &= mask;
                p3 &= mask;
                mask = (byte)~mask;
                m_planes[0][m_dst] &= mask;
                m_planes[0][m_dst] |= p0;
                m_planes[1][m_dst] &= mask;
                m_planes[1][m_dst] |= p1;
                m_planes[2][m_dst] &= mask;
                m_planes[2][m_dst] |= p2;
                m_planes[3][m_dst] &= mask;
                m_planes[3][m_dst] |= p3;
            }
            IncrementDest();
        }

        void IncLeftToRight ()
        {
            ++m_dst;
            ++m_x;
            if (m_x > m_info.iWidth)
                m_x = 0;
        }

        void IncTopToBottom ()
        {
            m_dst += m_stride;
            if (m_dst >= m_plane_size)
                m_dst = ++m_x;
        }

        internal void FlattenPlanes (int src, byte[] output)
        {
            int m_dst = 0;
            for (; src < m_plane_size; ++src)
            {
                int b0 = m_planes[0][src];
                int b1 = m_planes[1][src];
                int b2 = m_planes[2][src];
                int b3 = m_planes[3][src];
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
                    output[m_dst++] = px;
                }
            }
        }

        BitmapPalette ReadPalette ()
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte g = m_input.ReadUInt8();
                byte r = m_input.ReadUInt8();
                byte b = m_input.ReadUInt8();
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }
    }
}
