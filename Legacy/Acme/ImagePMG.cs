//! \file       ImagePMG.cs
//! \date       2018 Sep 23
//! \brief      Acme image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Image
{
    [Export(typeof(ImageFormat))]
    public class PmgFormat : ImageFormat
    {
        public override string         Tag { get { return "PMG"; } }
        public override string Description { get { return "Acme image format"; } }
        public override uint     Signature { get { return 0xA0; } }

        public PmgFormat ()
        {
            Signatures = new uint[] { 0xA0, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint width = file.Signature;
            if (width <= 0 || width > 0x800)
                return null;
            var header = file.ReadHeader (0x14);
            int bits = header.ToInt32 (8);
            int code_size = header.ToInt32 (12);
            int data_size = header.ToInt32 (16);
            if (bits <= 0 || code_size <= bits || data_size <= 0
                || code_size + data_size > file.Length)
                return null;
            file.Position = 0x14 + code_size + data_size;
            if (file.ReadUInt32() != width)
                return null;
            return new ImageMetaData {
                Width   = width * 4,
                Height  = header.ToUInt32 (4),
                BPP     = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PmgReader (file, info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PmgFormat.Write not implemented");
        }
    }

    internal class PmgReader
    {
        IBinaryStream   m_input;
        int             m_width;
        int             m_height;

        public PixelFormat    Format { get { return PixelFormats.Bgr24; } }

        public PmgReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
        }

        byte[]  m_line_buf = new byte[0x800];
        byte    m_bit_mask;

        public Array Unpack ()
        {
            int plane_size = m_width * m_height;
            var planes = new ushort[3 * plane_size / 2];
            int bsrc = 0;
            int gsrc = plane_size / 2;
            int rsrc = plane_size;

            m_input.Position = 0;
            m_bit_mask = 0x80;
            ReadPlane (planes, bsrc);
            ReadPlane (planes, gsrc);
            ReadPlane (planes, rsrc);

            var pixels = new byte[3 * plane_size];
            int dst = 0;
            while (dst < pixels.Length)
            {
                var b = planes[bsrc++];
                var g = planes[gsrc++];
                var r = planes[rsrc++];
                pixels[dst++] = (byte)b;
                pixels[dst++] = (byte)g;
                pixels[dst++] = (byte)r;
                pixels[dst++] = (byte)(b >> 8);
                pixels[dst++] = (byte)(g >> 8);
                pixels[dst++] = (byte)(r >> 8);
            }
            return pixels;
        }

        void ReadPlane (ushort[] output, int dst)
        {
            int blocks = m_input.ReadInt32();
            m_input.ReadInt32(); // height
            int bits_size = m_input.ReadInt32();
            int code_size = m_input.ReadInt32();
            int data_size = m_input.ReadInt32();
            var code = m_input.ReadBytes (code_size);
            var end_pos = m_input.Position + data_size;
            int bit_src = 0;
            int code_src = bits_size;
            for (int j = 0; j < m_line_buf.Length; ++j)
                m_line_buf[j] = 0;
            for (int y = 0; y < m_height; ++y)
            for (int x = 0; x < blocks; ++x)
            {
                if ((code[bit_src] & m_bit_mask) != 0)
                {
                    m_line_buf[x] ^= code[code_src++];
                }
                m_bit_mask >>= 1;
                if (0 == m_bit_mask)
                {
                    m_bit_mask = 0x80;
                    ++bit_src;
                }
                output[dst] = DecodePixel (m_line_buf[x] >> 4, output, dst);
                ++dst;
                output[dst] = DecodePixel (m_line_buf[x] & 0xF, output, dst);
                ++dst;
            }
            m_input.Position = end_pos;
        }

        ushort DecodePixel (int cmd, ushort[] output, int dst)
        {
            if (cmd != 0)
            {
                int offset = OffsetMap[cmd + 16] + m_width * OffsetMap[cmd];
                return output[dst - (offset >> 1)];
            }
            else
            {
                return m_input.ReadUInt16();
            }
        }

        static readonly byte[] OffsetMap = {
            0, 0, 0, 0, 1, 1, 2, 2, 2, 4, 4, 4, 8, 8, 8, 16, 0, 2, 4, 8, 0, 2, 0, 2, 4, 0, 2, 4, 0, 2, 4, 0
        };
    }
}
