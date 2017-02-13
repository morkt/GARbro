//! \file       ImageCHR.cs
//! \date       Mon Jan 16 07:53:42 2017
//! \brief      'Game System' character image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.GameSystem
{
    internal class ChrMetaData : ImageMetaData
    {
        public uint DataOffset;
        public int  RgbSize;
    }

    [Export(typeof(ImageFormat))]
    public class ChrFormat : ImageFormat
    {
        public override string         Tag { get { return "CHR"; } }
        public override string Description { get { return "'Game System' character image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature != file.Length)
                return null;
            var header = file.ReadHeader (0x18);
            int rgb_size = header.ToInt32 (4);
            if (rgb_size <= 0x20 || rgb_size > file.Length)
                return null;
            uint width = header.ToUInt32 (8);
            uint height = header.ToUInt32 (0xC);
            int x = header.ToInt32 (0x10);
            int y = header.ToInt32 (0x14);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000
                || x < 0 || x + width > 0x8000 || y < 0 || y + height > 0x8000)
                return null;
            return new ChrMetaData
            {
                Width = width,
                Height = height,
                OffsetX = x,
                OffsetY = y,
                BPP = 32,
                RgbSize = rgb_size,
                DataOffset = 0x20,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new ChrReader (file, (ChrMetaData)info);
            return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ChrFormat.Write not implemented");
        }
    }

    internal class ChrReader : BinaryImageDecoder
    {
        ChrMetaData     m_info;
        byte[]          m_output;
        int             m_stride;

        public byte[] Data { get { return m_output; } }
        public int  Stride { get { return m_stride; } }

        public ChrReader (IBinaryStream input, ChrMetaData info) : base (input, info)
        {
            m_info = info;
            m_stride = (int)m_info.Width * 4;
            m_output = new byte[m_stride * (int)m_info.Height];
        }

        protected override ImageData GetImageData ()
        {
            var pixels = Unpack();
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, Stride);
        }

        public byte[] Unpack ()
        {
            UnpackBaseline();
            if (m_info.RgbSize < m_input.Length)
            {
                m_input.Position = m_info.RgbSize;
                int overlay_length = m_input.ReadInt32();
                if (overlay_length > 0)
                    ReadOverlay();
            }
            return m_output;
        }

        public byte[] UnpackBaseline ()
        {
            m_input.Position = m_info.DataOffset;
            UnpackRgb ((int)m_info.Height);
            return m_output;
        }

        void UnpackRgb (int row_count)
        {
            int row = 0;
            while (row_count --> 0)
            {
                int dst = row;
                int x = 0;
                for (;;)
                {
                    int ctl = m_input.ReadUInt8();
                    if (ctl < 0x7F)
                    {
                        int alpha = -(2 * ctl - 0xFE);
                        m_input.Read (m_output, dst, 3);
                        m_output[dst+3] = (byte)~alpha;
                        dst += 4;
                        ++x;
                    }
                    else if (ctl < 0x9F)
                    {
                        int count = ctl - 0x7E;
                        x += count;
                        m_input.Read (m_output, dst, 3);
                        m_output[dst+3] = 0xFF;
                        count *= 4;
                        Binary.CopyOverlapped (m_output, dst, dst+4, count-4);
                        dst += count;
                    }
                    else if (0xFF == ctl)
                        break;
                    else
                    {
                        int count = ctl - 0x9E;
                        dst += count * 4;
                        x += count;
                    }
                }
                row += m_stride;
            }
        }

        void ReadOverlay ()
        {
            m_input.ReadInt32();
            int frame_count = m_input.ReadInt32();
            if (frame_count <= 0)
                return;
            int x = m_input.ReadInt16() - m_info.OffsetX;
            int y = m_input.ReadInt16();
            int w = m_input.ReadInt16();
            int h = m_input.ReadInt16();
            y = (int)m_info.Height + m_info.OffsetY - y - h;
            if (x < 0 || y < 0)
                return;
            int output = y * m_stride + x * 4;
            for (int i = 0; i < h; ++i)
            {
                int dst = output;
                for (int j = 0; j < w; ++j)
                {
                    m_input.Read (m_output, dst, 3);
                    int a = m_input.ReadByte();
                    m_output[dst+3] = (byte)(a * 0xFF / 0x80);
                    dst += 4;
                }
                output += m_stride;
            }
        }
    }
}
