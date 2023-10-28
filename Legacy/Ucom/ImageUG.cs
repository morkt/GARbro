//! \file       ImageUG.cs
//! \date       2023 Oct 16
//! \brief      Ucom image format (PC-98).
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

// [961220][Ucom] Bunkasai

namespace GameRes.Formats.Ucom
{
    [Export(typeof(ImageFormat))]
    public class UgFormat : ImageFormat
    {
        public override string         Tag => "UG";
        public override string Description => "Ucom image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".UG"))
                return null;
            int left   = file.ReadUInt16();
            int top    = file.ReadUInt16();
            int right  = file.ReadUInt16();
            int bottom = file.ReadUInt16();
            int width = (right - left + 1) << 3;
            int height = bottom - top + 1;
            if (width <= 0 || height <= 0 || width > 640 || height > 512)
                return null;
            return new ImageMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = left,
                OffsetY = top,
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new UgReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("UgFormat.Write not implemented");
        }
    }

    /// <summary>
    /// Same compression algorithm as the base, but scanlines are vertical
    /// </summary>
    internal class UgReader : System98.GraBaseReader
    {
        public UgReader (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        public ImageData Unpack ()
        {
            m_input.Position = 8;
            var palette = ReadPalette();
            m_input.Position = 0x28;
            try
            {
                UnpackBitsInternal();
            }
            catch (EndOfStreamException)
            {
                FlushBuffer();
            }
            return ImageData.Create (m_info, PixelFormats.Indexed4, palette, Pixels, Stride);
        }

        void UnpackBitsInternal ()
        {
            int height = m_info.iHeight;
            int hTimes2 = height << 1;
            int hTimes4 = height << 2;
            int buffer_size = hTimes4 * 3;
            m_buffer = new ushort[buffer_size >> 1];
            m_dst = 0;
            InitFrame();
            InitBitReader();
            ushort p = ReadPair (0);
            for (int i = 0; i < hTimes2+1; ++i)
                m_buffer[i] = p;
            int dst = hTimes4;
            int prev_src = 0;
            while (m_dst < m_pixels.Length)
            {
                bool same_line = false;
                int src = -hTimes2;
                if (GetNextBit() != 0) // @1@
                {
                    if (GetNextBit() == 0) // @4@
                        src += 4;
                    else if (GetNextBit() == 0) // @5@
                        src -= 4;
                    else
                        src <<= 1;
                }
                else if (GetNextBit() == 0) // @2@
                {
                    src = -4;
                    p = m_buffer[dst/2-1];
                    if ((p & 0xFF) == (p >> 8))
                        same_line = src != prev_src;
                }
                if (src != prev_src) // @6@
                {
                    prev_src = src;
                    if (!same_line)
                        src += dst;
                    else
                        src = dst - 2;
                    if (GetNextBit() != 0) // @3@
                    {
                        int bitlength = 0;
                        do
                        {
                            ++bitlength;
                        }
                        while (GetNextBit() != 0);
                        int count = 1;
                        while (bitlength --> 0)
                            count = count << 1 | GetNextBit();
                        MovePixels (m_buffer, src, dst, count);
                        dst += count << 1;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = hTimes4;
                        }
                    }
                    else
                    {
                        MovePixels (m_buffer, src, dst, 1);
                        dst += 2;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = hTimes4;
                        }
                    }
                }
                else
                {
                    p = m_buffer[dst/2-1];
                    do
                    {
                        byte prev = (byte)(p >> 8);
                        p = ReadPair (prev);
                        m_buffer[dst >> 1] = p;
                        dst += 2;
                        if (dst == buffer_size)
                        {
                            if (FlushBuffer())
                                return;
                            dst = hTimes4;
                        }
                    }
                    while (GetNextBit() != 0);
                    prev_src = 0;
                }
            }
        }

        bool FlushBuffer ()
        {
            int height   = m_info.iHeight;
            int src_line = height << 1;
            int dst      = m_dst;
            for (int i = 0; i < height; ++i)
            {
                int src = src_line;
                for (int j = 0; j < 4; ++j)
                {
                    ushort p = m_buffer[src];
                    m_pixels[dst+j] = (byte)((p & 0xF0) | p >> 12);
                    src += height;
                }
                src_line++;
                dst += m_output_stride;
            }
            m_dst += 4;
            MovePixels (m_buffer, height << 3, 0, height << 1);
            return m_dst >= m_output_stride;
        }

        BitmapPalette ReadPalette ()
        {
            var colors = new Color[16];
            for (int i = 0; i < 16; ++i)
            {
                ushort rgb = m_input.ReadUInt16();
                int b = (rgb & 0xF) * 0x11;
                int r = ((rgb >> 4) & 0xF) * 0x11;
                int g = ((rgb >> 8) & 0xF) * 0x11;
                colors[i] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
            }
            return new BitmapPalette (colors);
        }
    }
}
