//! \file       ImageBPA.cs
//! \date       2019 Mar 12
//! \brief      Liddell image format.
//
// Copyright (C) 2019 by morkt
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

namespace GameRes.Formats.Liddell
{
    internal class BpaMetaData : ImageMetaData
    {
        public int  Colors;
        public int  PaletteOffset;
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BpaFormat : ImageFormat
    {
        public override string         Tag { get { return "BPA"; } }
        public override string Description { get { return "Liddell image format"; } }
        public override uint     Signature { get { return 0x4150422D; } } // '-BPA-'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x11);
            if (header[4] != '-')
                return null;
            int palette_offset = header.ToUInt16 (0xC);
            return new BpaMetaData {
                Width  = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                BPP    = 0 == palette_offset ? header[0x10] * 8 : 8,
                Colors = header.ToUInt16 (0xA),
                PaletteOffset = palette_offset,
                DataOffset = header.ToUInt16 (0xE),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new BpaDecoder (file, (BpaMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BpaFormat.Write not implemented");
        }
    }

    internal class BpaDecoder
    {
        IBinaryStream   m_input;
        BpaMetaData     m_info;
        int             m_alignedWidth;

        public BpaDecoder (IBinaryStream input, BpaMetaData info)
        {
            m_input = input;
            m_info = info;
            m_alignedWidth = 4 * ((m_info.iWidth - 1) / 4 + 1);
        }

        public ImageData Unpack ()
        {
            BitmapPalette palette = null;
            if (m_info.PaletteOffset != 0)
            {
                m_input.Position = m_info.PaletteOffset;
                palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors, PaletteFormat.Rgb);
            }
            m_input.Position = m_info.DataOffset;
            int stride = m_alignedWidth * m_info.BPP / 8;
            var pixels = new byte[stride * m_info.iHeight];
            int channels = m_info.BPP / 8;
            var channel = pixels;
            if (channels > 1)
                channel = new byte[m_alignedWidth * m_info.iHeight];
            for (int i = 0; i < channels; ++i)
            {
                Decompress (channel);
                if (channels > 1)
                {
                    int dst_row = i;
                    int src_row = channel.Length - m_alignedWidth;
                    for (int y = 0; y < m_info.iHeight; ++y)
                    {
                        int dst = dst_row;
                        int src = src_row;
                        for (int x = 0; x < m_alignedWidth; ++x)
                        {
                            pixels[dst] = channel[src++];
                            dst += channels;
                        }
                        dst_row += stride;
                        src_row -= m_alignedWidth;
                    }
                    m_input.ReadByte();
                }
            }
            if (8 == m_info.BPP)
                return ImageData.Create (m_info, PixelFormats.Indexed8, palette, pixels, m_alignedWidth);
            PixelFormat format = 24 == m_info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            stride = ((m_info.iWidth * m_info.BPP / 8) + 3) & ~3;
            return ImageData.CreateFlipped (m_info, format, palette, pixels, stride);
        }

        void Decompress (byte[] output)
        {
            m_pixelStack[0] = 0;
            m_pixelStack[4] = 0;
            m_pixelStack[5] = 0;
            int dst = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                byte ctlBits = 0;
                int ctlCount = 0;
                int w = m_alignedWidth;
                while (w > 0)
                {
                    if (0 == ctlCount)
                    {
                        ctlCount = 4;
                        ctlBits = m_input.ReadUInt8();
                    }
                    int chunk_size = Math.Min (w, 16);
                    switch ((ctlBits >> 6) & 3)
                    {
                    case 0:
                        m_input.Read (output, dst, chunk_size);
                        dst += chunk_size;
                        break;

                    case 1:
                        {
                            byte val = m_input.ReadUInt8();
                            chunk_size = m_input.ReadUInt8();
                            for (int i = 0; i < chunk_size; ++i)
                            {
                                output[dst++] = val;
                            }
                            StorePixel (val);
                            break;
                        }
                    case 2:
                        {
                            ushort ctl = m_input.ReadUInt16();
                            byte val = m_input.ReadUInt8();
                            for (int i = 0; i < chunk_size; ++i)
                            {
                                if ((ctl & 0x8000) != 0)
                                    output[dst++] = val;
                                else
                                    output[dst++] = m_input.ReadUInt8();
                                ctl <<= 1;
                            }
                            StorePixel (val);
                            break;
                        }
                    case 3:
                        {
                            m_bitCount = 0;
                            for (int i = 0; i < chunk_size; ++i)
                            {
                                output[dst++] = RestorePixel();
                            }
                            if (m_bitCount != 8)
                                m_input.ReadByte();
                            break;
                        }
                    }
                    w -= chunk_size;
                    --ctlCount;
                    ctlBits <<= 2;
                }
            }
        }

        byte[] m_pixelStack = new byte[6];

        void StorePixel (byte val)
        {
            int i;
            for (i = 0; i < 5; ++i)
            {
                if (m_pixelStack[i] == val)
                    break;
            }
            if (i == 0)
                return;
            do
            {
                m_pixelStack[i] = m_pixelStack[i-1];
            }
            while (--i > 0);
            m_pixelStack[0] = val;
        }

        byte RestorePixel ()
        {
            byte bits = GetNextBit (0);
            if (0 == bits)
                return m_pixelStack[0];

            byte result = 0;
            int count = 0;
            bits = GetNextBit (bits);
            if (2 == bits)
            {
                result = m_pixelStack[1];
                count = 1;
            }
            else if (GetNextBit (bits) == 6)
            {
                bits = GetNextBit (0);
                switch (GetNextBit (bits))
                {
                case 0: count = 2; break;
                case 1: count = 3; break;
                case 2: count = 4; break;
                case 3: count = 5; break;
                }
                result = m_pixelStack[count];
            }
            else
            {
                for (int i = 0; i < 8; ++i)
                {
                    result = GetNextBit (result);
                }
                count = 5;
            }
            for (int i = count; i > 0; --i)
                m_pixelStack[i] = m_pixelStack[i-1];
            m_pixelStack[0] = result;
            return result;
        }

        int     m_bitCount;
        byte    m_curBits;

        byte GetNextBit (byte prev)
        {
            if (m_bitCount == 0)
                m_curBits = m_input.ReadUInt8();

            int result = prev << 1 | m_curBits >> 7;
            m_curBits <<= 1;
            if (++m_bitCount >= 8)
                m_bitCount = 0;
            return (byte)result;
        }
    }
}
