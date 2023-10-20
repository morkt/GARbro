//! \file       ImageMI2.cs
//! \date       2023 Oct 19
//! \brief      Mapl engine image format.
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

// [980130][Pias] Rashin

namespace GameRes.Formats.Mapl
{
    internal class Mi2MetaData : ImageMetaData
    {
        public int  Colors;
    }

    [Export(typeof(ImageFormat))]
    public class Mi2Format : ImageFormat
    {
        public override string         Tag => "MI2";
        public override string Description => "Mapl engine image format";
        public override uint     Signature => 0x28;

        public Mi2Format ()
        {
            Extensions = new[] { "mi2", "fcg" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28); // BITMAPINFOHEADER
            int bpp = header.ToUInt16 (0xE);
            if (bpp != 8 && bpp != 24)
                return null;
            uint width  = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            int colors = header.ToInt32 (0x20);
            if (8 == bpp)
            {
                if (colors < 0 || colors > 0x100)
                    return null;
                if (0 == colors)
                    colors = 0x100;
            }
            return new Mi2MetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                Colors = colors,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Mi2Reader (file, (Mi2MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mi2Format.Write not implemented");
        }
    }

    internal class Mi2Reader
    {
        IBinaryStream   m_input;
        Mi2MetaData     m_info;

        public Mi2Reader (IBinaryStream file, Mi2MetaData info)
        {
            m_input = file;
            m_info = info;
        }

        byte[] m_block = new byte[64];
        byte[] m_buffer = new byte[32];

        int stride;
        int block_stride;
        int blocksW;
        int blocksH;

        public ImageData Unpack ()
        {
            stride = (m_info.iWidth + 7) & ~7;
            block_stride = stride * 8;
            blocksW = m_info.iWidth >> 3;
            if ((m_info.iWidth & 7) != 0)
                blocksW++;
            blocksH = m_info.iHeight >> 3;
            if ((m_info.iHeight & 7) != 0)
                blocksH++;
            var channel = new byte[stride * m_info.iHeight];
            m_input.Position = 0x28;
            if (8 == m_info.BPP)
            {
                var palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors);
                UnpackChannel (channel);
                return ImageData.Create (m_info, PixelFormats.Indexed8, palette, channel, stride);
            }
            else
            {
                int bgr_stride = m_info.iWidth * 3;
                var bgr = new byte[bgr_stride * m_info.iHeight];
                for (int c = 0; c < 3; ++c)
                {
                    UnpackChannel (channel);
                    int src = 0;
                    int dst = c;
                    for (int y = 0; y < m_info.iHeight; ++y)
                    {
                        for (int x = 0; x < m_info.iWidth; ++x)
                        {
                            bgr[dst] = channel[src+x];
                            dst += 3;
                        }
                        src += stride;
                    }
                }
                return ImageData.Create (m_info, PixelFormats.Bgr24, null, bgr, bgr_stride);
            }
        }

        void UnpackChannel (byte[] output)
        {
            int dst_row = output.Length - stride;
            for (int y = 0; y < blocksH; ++y)
            {
                int dst_pos = dst_row;
                for (int x = 0; x < blocksW; ++x)
                {
                    byte ctl = m_input.ReadUInt8();
                    switch (ctl)
                    {
                    case 0:
                        m_input.Read (m_block, 0, m_block.Length);
                        break;
                    case 1:
                        {
                            byte b = m_input.ReadUInt8();
                            for (int i = 0; i < m_block.Length; ++i)
                                m_block[i] = b;
                            break;
                        }
                    case 2:
                        Op2();
                        break;
                    case 3:
                        Op3();
                        break;
                    case 4:
                        Op4();
                        break;
                    case 5:
                        Op5();
                        break;
                    case 6:
                        Op6();
                        break;
                    case 7:
                        Op7();
                        break;
                    case 8:
                        Op8();
                        AdjustBlock();
                        break;
                    case 9:
                        Op9();
                        AdjustBlock();
                        break;
                    case 10:
                        Op4();
                        AdjustBlock();
                        break;
                    case 11:
                        Op5();
                        AdjustBlock();
                        break;
                    case 12:
                        Op6();
                        AdjustBlock();
                        break;
                    case 13:
                        Op7();
                        AdjustBlock();
                        break;
                    default:
                        throw new InvalidFormatException();
                    }
                    int dst = dst_pos;
                    int src = 0;
                    while (src < m_block.Length && dst >= 0)
                    {
                        Buffer.BlockCopy (m_block, src, output, dst, 8);
                        src += 8;
                        dst -= stride;
                    }
                    dst_pos += 8;
                }
                dst_row -= block_stride;
            }
        }

        void Op2 ()
        {
            byte b = m_input.ReadUInt8();
            m_input.Read (m_buffer, 0, 8);
            int pos = 0;
            for (int i = 0; i < 8; ++i)
            {
                byte mask = 0x80;
                for (int j = 0; j < 8; ++j)
                {
                    if ((mask & m_buffer[i]) != 0)
                        m_block[pos++] = b;
                    else
                        m_block[pos++] = m_input.ReadUInt8();
                    mask >>= 1;
                }
            }
        }

        void Op3 ()
        {
            int pos = 0;
            byte b1 = m_input.ReadUInt8();
            byte b2 = m_input.ReadUInt8();
            for (int i = 0; i < 8; ++i)
            {
                byte mask = 0x80;
                byte b = m_input.ReadUInt8();
                for (int j = 0; j < 8; ++j)
                {
                    if ((mask & b) != 0)
                        m_block[pos++] = b2;
                    else
                        m_block[pos++] = b1;
                    mask >>= 1;
                }
            }
        }

        void Op4()
        {
            byte b1 = m_input.ReadUInt8();
            byte b2 = m_input.ReadUInt8();
            byte b3 = m_input.ReadUInt8();
            int dst = 0;
            m_input.Read (m_buffer, 0, 16);
            for (int i = 0; i < 16; ++i)
            {
                byte bits = m_buffer[i];
                for (int j = 0; j < 4; ++j)
                {
                    switch (bits >> 6)
                    {
                    case 0: m_block[dst] = m_input.ReadUInt8(); break;
                    case 1: m_block[dst] = b1; break;
                    case 2: m_block[dst] = b2; break;
                    case 3: m_block[dst] = b3; break;
                    }
                    dst++;
                    bits <<= 2;
                }
            }
        }

        void Op5()
        {
            byte b0 = m_input.ReadUInt8();
            byte b1 = m_input.ReadUInt8();
            byte b2 = m_input.ReadUInt8();
            byte b3 = m_input.ReadUInt8();
            m_input.Read (m_buffer, 0, 16);
            int dst = 0;
            for (int i = 0; i < 16; ++i)
            {
                byte bits = m_buffer[i];
                for (int j = 0; j < 4; ++j)
                {
                    switch (bits >> 6)
                    {
                    case 0: m_block[dst] = b0; break;
                    case 1: m_block[dst] = b1; break;
                    case 2: m_block[dst] = b2; break;
                    case 3: m_block[dst] = b3; break;
                    }
                    dst++;
                    bits <<= 2;
                }
            }
        }

        byte[] m_buffer2 = new byte[0x100];
        byte[] m_buffer6 = new byte[0x40];

        void Op6()
        {
            int count = m_input.ReadUInt8();
            m_input.Read (m_buffer2, 0, count);
            m_input.Read (m_buffer, 0, 24);
            for (int i = 0; i < m_buffer6.Length; ++i)
                m_buffer6[i] = 0;
            int buf_pos = 0;
            int bits = 0;
            int bit_count = 0;
            int bit_src = 0;
            byte mask = 0x80;
            for (int i = 0; i < 64; ++i)
            {
                byte n0 = 0;
                byte n1 = 4;
                for (int j = 0; j < 3; ++j)
                {
                    if (0 == bit_count)
                    {
                        bits = m_buffer[bit_src++];
                        bit_count = 8;
                    }
                    if ((bits & mask) != 0)
                        n0 |= n1;
                    --bit_count;
                    n1 >>= 1;
                    mask = Binary.RotByteR (mask, 1);
                }
                m_buffer6[buf_pos++] = n0;
            }
            for (int i = 0; i < 64; ++i)
            {
                byte b = m_buffer6[i];
                if (b > 0)
                    m_block[i] = m_buffer2[b-1];
                else
                    m_block[i] = m_input.ReadUInt8();
            }
        }

        void Op7()
        {
            int count = m_input.ReadUInt8();
            m_input.Read (m_buffer2, 0, count);
            m_input.Read (m_buffer, 0, 32);
            int dst = 0;
            for (int i = 0; i < 32; ++i)
            {
                byte b = m_buffer[i];
                for (int j = 0; j < 2; ++j)
                {
                    int bits = b >> 4;
                    b <<= 4;
                    if (bits != 0)
                        m_block[dst++] = m_buffer2[bits-1];
                    else
                        m_block[dst++] = m_input.ReadUInt8();
                }
            }
        }

        void Op8()
        {
            byte b0 = m_input.ReadUInt8();
            m_input.Read (m_buffer, 0, 8);
            int src = 0;
            int dst = 0;
            for (int i = 0; i < 8; ++i)
            {
                byte mask = 0x80;
                byte b = m_buffer[src++];
                for (int j = 0; j < 8; ++j)
                {
                    if ((mask & b) != 0)
                        m_block[dst++] = b0;
                    else
                        m_block[dst++] = m_input.ReadUInt8();
                    mask >>= 1;
                }
            }
        }

        void Op9()
        {
            byte b0 = m_input.ReadUInt8();
            byte b1 = m_input.ReadUInt8();
            int dst = 0;
            for (int i = 0; i < 8; ++i)
            {
                byte mask = 0x80;
                byte b = m_input.ReadUInt8();
                for (int j = 0; j < 8; ++j)
                {
                    if ((mask & b) != 0)
                        m_block[dst++] = b1;
                    else
                        m_block[dst++] = b0;
                    mask >>= 1;
                }
            }
        }

        void AdjustBlock()
        {
            int dst = 0;
            for (int i = 0; i < 8; ++i)
            {
                m_input.ReadByte();
                for (int j = 0; j < 8; ++j)
                {
                    m_block[dst++] <<= 1;
                }
            }
        }
    }
}
