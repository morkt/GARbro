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

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20).ToArray();
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

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new XtxReader (stream.AsStream, (XtxMetaData)info);
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
            var dxt = new DirectDraw.DxtDecoder (packed, m_info);
            m_output = dxt.UnpackDXT5();
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
    }
}
