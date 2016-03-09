//! \file       ImageXTX.cs
//! \date       Mon Feb 29 19:14:55 2016
//! \brief      Xbox 360 texture.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Cri
{
    internal class XtxMetaData : ImageMetaData
    {
        public byte Format;
        public uint DataOffset;
        public int  AlignedWidth;
        public int  AlignedHeight;
    }

    [Export(typeof(ImageFormat))]
    public class XtxFormat : ImageFormat
    {
        public override string         Tag { get { return "XTX"; } }
        public override string Description { get { return "Xbox 360 texture format"; } }
        public override uint     Signature { get { return 0x00787478; } } // 'xtx'

        public XtxFormat ()
        {
            Signatures = new uint[] { 0x00787478, 0 };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x20];
            if (0x20 != stream.Read (header, 0, 0x20))
                return null;
            if (!Binary.AsciiEqual (header, 0, "xtx\0"))
            {
                var header_size = LittleEndian.ToUInt32 (header, 0);
                if (header_size >= 0x1000) // XXX use some arbitrary "large" value to avoid call to Stream.Length
                    return null;
                stream.Position = header_size;
                if (0x20 != stream.Read (header, 0, 0x20))
                    return null;
                if (!Binary.AsciiEqual (header, 0, "xtx\0"))
                    return null;
            }
            if (header[4] > 2)
                return null;
            int aligned_width  = BigEndian.ToInt32 (header, 8);
            int aligned_height = BigEndian.ToInt32 (header, 0xC);
            if (aligned_width <= 0 || aligned_height <= 0)
                return null;
            return new XtxMetaData
            {
                Width   = BigEndian.ToUInt32 (header, 0x10),
                Height  = BigEndian.ToUInt32 (header, 0x14),
                OffsetX = BigEndian.ToInt32 (header, 0x18),
                OffsetY = BigEndian.ToInt32 (header, 0x1C),
                BPP     = 32,
                Format  = header[4],
                AlignedWidth  = aligned_width,
                AlignedHeight = aligned_height,
                DataOffset  = (uint)stream.Position,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new XtxReader (stream, (XtxMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("XtxFormat.Write not implemented");
        }
    }

    internal sealed class XtxReader
    {
        Stream      m_input;
        int         m_width;
        int         m_height;
        XtxMetaData m_info;
        byte[]      m_output;
        int         m_output_stride;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public XtxReader (Stream input, XtxMetaData info)
        {
            m_input = input;
            m_info = info;
            m_input.Position = info.DataOffset;
            m_width = (int)m_info.Width;
            m_output_stride = m_width * 4;
            m_height = (int)m_info.Height;
            m_output = new byte[m_output_stride*m_height];
        }

        public byte[] Unpack ()
        {
            Format = PixelFormats.Bgra32;
            if (0 == m_info.Format)
                ReadTex0();
            else if (1 == m_info.Format)
                ReadTex1();
            else
                ReadTex2();
            return m_output;
        }

        void ReadTex0 ()
        {
            int total = m_info.AlignedWidth * m_info.AlignedHeight;
            var texture = new byte[total*4];
            m_input.Read (texture, 0, texture.Length);
            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, m_info.AlignedWidth, 4);
                int x = GetX (i, m_info.AlignedWidth, 4);
                if (y < m_height && x < m_width)
                {
                    int dst = m_output_stride * y + x * 4;
                    m_output[dst]   = texture[src+3];
                    m_output[dst+1] = texture[src+2];
                    m_output[dst+2] = texture[src+1];
                    m_output[dst+3] = texture[src];
                }
                src += 4;
            }
        }

        void ReadTex1 ()
        {
            int total = m_info.AlignedWidth * m_info.AlignedHeight;
            var texture = new byte[total*2];
            var packed = new byte[total*2];
            m_input.Read (texture, 0, texture.Length);
            int stride = m_info.AlignedWidth;
            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, m_info.AlignedWidth, 2);
                int x = GetX (i, m_info.AlignedWidth, 2);
                int dst = (x + y * stride) * 2;
                packed[dst]   = texture[src+1];
                packed[dst+1] = texture[src];
                src += 2;
            }
            Format = PixelFormats.Bgr565;
            m_output = packed;
            throw new NotImplementedException ("XTX textures format 1 not implemented");
        }

        void ReadTex2 ()
        {
            int tex_width = m_info.AlignedWidth >> 2;
            int total = tex_width * (m_info.AlignedHeight >> 2);
            var texture = new byte[m_info.AlignedWidth * m_info.AlignedHeight];
            var packed = new byte[m_info.AlignedWidth * m_info.AlignedHeight];
            m_input.Read (texture, 0, texture.Length);
            int src = 0;
            for (int i = 0; i < total; ++i)
            {
                int y = GetY (i, tex_width, 0x10);
                int x = GetX (i, tex_width, 0x10);
                int dst = (x + y * tex_width) * 16;
                for (int j = 0; j < 8; ++j)
                {
                    packed[dst++] = texture[src+1];
                    packed[dst++] = texture[src];
                    src += 2;
                }
            }
            UnpackDXT5 (packed);
        }

        static int GetY (int i, int width, byte level)
        {
            int v1 = (level >> 2) + (level >> 1 >> (level >> 2));
            int v2 = i << v1;
            int v3 = (v2 & 0x3F) + ((v2 >> 2) & 0x1C0) + ((v2 >> 3) & 0x1FFFFE00);
            return ((v3 >> 4) & 1)
                + ((((v3 & ((level << 6) - 1) & -0x20)
                     + ((((v2 & 0x3F) + ((v2 >> 2) & 0xC0)) & 0xF) << 1)) >> (v1 + 3)) & -2)
                + ((((v2 >> 10) & 2) + ((v3 >> (v1 + 6)) & 1)
                    + (((v3 >> (v1 + 7)) / ((width + 31) >> 5)) << 2)) << 3);
        }

        static int GetX (int i, int width, byte level)
        {
            int v1 = (level >> 2) + (level >> 1 >> (level >> 2));
            int v2 = i << v1;
            int v3 = (v2 & 0x3F) + ((v2 >> 2) & 0x1C0) + ((v2 >> 3) & 0x1FFFFE00);
            return ((((level << 3) - 1) & ((v3 >> 1) ^ (v3 ^ (v3 >> 1)) & 0xF)) >> v1)
                + ((((((v2 >> 6) & 0xFF) + ((v3 >> (v1 + 5)) & 0xFE)) & 3)
                    + (((v3 >> (v1 + 7)) % (((width + 31)) >> 5)) << 2)) << 3);
        }

        void UnpackDXT5 (byte[] input)
        {
            int src = 0;
            for (int y = 0; y < m_info.AlignedHeight; y += 4)
            for (int x = 0; x < m_info.AlignedWidth; x += 4)
            {
                DecompressDXT5Block (input, src, y, x);
                src += 16;
            }
        }

        byte[] m_dxt5_alpha = new byte[16];

        void DecompressDXT5Block (byte[] input, int src, int block_y, int block_x)
        {
            byte alpha0 = input[src];
            byte alpha1 = input[src+1];

            DecompressDXT5Alpha (input, src+2, m_dxt5_alpha);

            ushort color0 = LittleEndian.ToUInt16 (input, src+8);
            ushort color1 = LittleEndian.ToUInt16 (input, src+10);

            int t = (color0 >> 11) * 255 + 16;
            byte r0 = (byte)((t / 32 + t) / 32);
            t = ((color0 & 0x07E0) >> 5) * 255 + 32;
            byte g0 = (byte)((t / 64 + t) / 64);
            t = (color0 & 0x001F) * 255 + 16;
            byte b0 = (byte)((t / 32 + t) / 32);

            t = (color1 >> 11) * 255 + 16;
            byte r1 = (byte)((t / 32 + t) / 32);
            t = ((color1 & 0x07E0) >> 5) * 255 + 32;
            byte g1 = (byte)((t / 64 + t) / 64);
            t = (color1 & 0x001F) * 255 + 16;
            byte b1 = (byte)((t / 32 + t) / 32);

            uint code = LittleEndian.ToUInt32 (input, src+12);

            for (int y = 0; y < 4 && (block_y + y) < m_height; ++y)
            for (int x = 0; x < 4 && (block_x + x) < m_width; ++x)
            {
                int alpha_code = m_dxt5_alpha[4 * y + x];
                byte alpha;
                if (0 == alpha_code)
                    alpha = alpha0;
                else if (1 == alpha_code)
                    alpha = alpha1;
                else if (alpha0 > alpha1)
                    alpha = (byte)(((8 - alpha_code) * alpha0 + (alpha_code - 1) * alpha1) / 7);
                else if (6 == alpha_code)
                    alpha = 0;
                else if (7 == alpha_code)
                    alpha = 0xFF;
                else
                    alpha = (byte)(((6 - alpha_code) * alpha0 + (alpha_code - 1) * alpha1) / 5);

                int dst = m_output_stride * (block_y + y) + (block_x + x) * 4;
                switch (code & 3)
                {
                case 0:
                    PutPixel (dst, r0, g0, b0, alpha);
                    break;
                case 1:
                    PutPixel (dst, r1, g1, b1, alpha);
                    break;
                case 2:
                    PutPixel (dst, (byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3), alpha);
                    break;
                case 3:
                    PutPixel (dst, (byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3), alpha);
                    break;
                }
                code >>= 2;
            }
        }

        static void DecompressDXT5Alpha (byte[] input, int src, byte[] output)
        {
            int dst = 0;
            for (int j = 0; j < 2; ++j)
            {
                int block = input[src++];
                block |= input[src++] << 8;
                block |= input[src++] << 16;

                for (int i = 0; i < 8; ++i)
                {
                    output[dst++] = (byte)(block & 7);
                    block >>= 3;
                }
            }
        }

        void PutPixel (int dst, byte r, byte g, byte b, byte a)
        {
            m_output[dst]   = b;
            m_output[dst+1] = g;
            m_output[dst+2] = r;
            m_output[dst+3] = a;
        }
    }
}
