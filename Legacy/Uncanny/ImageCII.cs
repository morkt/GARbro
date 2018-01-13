//! \file       ImageCII.cs
//! \date       2018 Jan 12
//! \brief      Uncanny image format.
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
using System.Windows.Media.Imaging;

// [000825][Uncanny!] Camera Eyes

namespace GameRes.Formats.Uncanny
{
    internal class CiiMetaData : ImageMetaData
    {
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class CiiFormat : ImageFormat
    {
        public override string         Tag { get { return "CII"; } }
        public override string Description { get { return "Uncanny image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            int type = header.ToUInt16 (0);
            int bpp;
            if (5 == type)
                bpp = 24;
            else if (3 == (type & 0x7FFF))
                bpp = 8;
            else if (2 == (type & 0x7FFF))
                bpp = 4;
            else
                return null;
            int w = header.ToInt16 (2);
            int h = header.ToInt16 (4);
            if (w <= 0 || h <= 0)
                return null;
            if (bpp <= 8)
            {
                int colors = 1 << bpp;
                if (colors < header.ToUInt16 (6))
                    return null;
            }
            return new CiiMetaData { 
                Width = (uint)w,
                Height = (uint)h,
                BPP = bpp,
                IsCompressed = (header[1] & 0x80) != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CiiReader (file, (CiiMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CiiFormat.Write not implemented");
        }
    }

    internal class CiiReader
    {
        IBinaryStream   m_input;
        CiiMetaData     m_info;
        int             m_stride;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return m_stride; } }
        public byte[]           Data { get { return m_output; } }

        public CiiReader (IBinaryStream input, CiiMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = (int)info.Width * info.BPP / 8;
            m_output = new byte[m_stride * (((int)m_info.Height + 1) & ~1)];
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else
                Format = PixelFormats.Indexed4;
        }

        public void Unpack ()
        {
            m_input.Position = 6;
            if (m_info.BPP <= 8)
            {
                int colors = m_input.ReadUInt16();
                Palette = ImageFormat.ReadPalette (m_input.AsStream, colors);
                if (m_info.IsCompressed)
                    UnpackRle();
                else
                    m_input.Read (m_output, 0, m_output.Length);
            }
            else
                Unpack24bpp();
        }

        void UnpackRle ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count = m_input.ReadByte();
                if (-1 == count)
                    break;
                if (0 != (count & 0x80))
                {
                    byte v = m_input.ReadUInt8();
                    count = (count & 0x7F) + 1;
                    while (count --> 0)
                        m_output[dst++] = v;
                }
                else
                {
                    count++;
                    m_input.Read (m_output, dst, count);
                    dst += count;
                }
            }
        }

        void Unpack24bpp ()
        {
            int blocks_w = (int)m_info.Width / 2;
            int blocks_h = (int)m_info.Height / 2;
            int dst1 = 0;
            int dst2 = m_stride;
            for (int y = 0; y < blocks_h; ++y)
            {
                for (int x = 0; x < blocks_w; ++x)
                {
                    sbyte v1 = m_input.ReadInt8();
                    sbyte v2 = m_input.ReadInt8();
                    int b = (29145 * v2 - 21601 * v1) >> 14;
                    int g = (-5312 * v1 - 11083 * v2) >> 14;
                    int r = (3 * (v1 + 24 * (v1 + 2 * (v1 + (v1 << 7)))) + 10638 * v2) >> 14;
                    byte x00 = m_input.ReadUInt8();
                    m_output[dst1++] = Clamp (x00 + b);
                    m_output[dst1++] = Clamp (x00 + g);
                    m_output[dst1++] = Clamp (x00 + r);
                    byte x01 = m_input.ReadUInt8();
                    m_output[dst1++] = Clamp (x01 + b);
                    m_output[dst1++] = Clamp (x01 + g);
                    m_output[dst1++] = Clamp (x01 + r);
                    byte x10 = m_input.ReadUInt8();
                    m_output[dst2++] = Clamp (x10 + b);
                    m_output[dst2++] = Clamp (x10 + g);
                    m_output[dst2++] = Clamp (x10 + r);
                    byte x11 = m_input.ReadUInt8();
                    m_output[dst2++] = Clamp (x11 + b);
                    m_output[dst2++] = Clamp (x11 + g);
                    m_output[dst2++] = Clamp (x11 + r);
                }
                dst1 += m_stride;
                dst2 += m_stride;
            }
        }

        static byte Clamp (int v)
        {
            if (v < 0)
                return 0;
            else if (v > 0xFF)
                return 0xFF;
            return (byte)v;
        }
    }
}
