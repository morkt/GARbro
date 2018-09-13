//! \file       ImageGBP.cs
//! \date       2018 Aug 27
//! \brief      SVIU System image format.
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
using GameRes.Utility;

namespace GameRes.Formats.Sviu
{
    internal class GbpMetaData : ImageMetaData
    {
        public int  HeaderSize;
        public int  DataOffset;
        public int  Method;
    }

    [Export(typeof(ImageFormat))]
    public class GbpFormat : ImageFormat
    {
        public override string         Tag { get { return "GBP"; } }
        public override string Description { get { return "SVIU system image format"; } }
        public override uint     Signature { get { return 0x50425947; } } // 'GYBP'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            file.Seek (-0x13, SeekOrigin.End);
            var key = file.ReadBytes (0x13);
            for (int i = 4; i < 0x14; i += 2)
            {
                header[i]   ^= key[0x10];
                header[i+1] ^= key[0x11];
            }
            for (int i = 0; i < 0x10; ++i)
            {
                header[i+4] -= key[i];
            }
            return new GbpMetaData {
                Width  = header.ToUInt16 (0xE),
                Height = header.ToUInt16 (0x10),
                BPP    = header.ToUInt16 (0x12),
                HeaderSize = header.ToInt32 (4),
                DataOffset = header.ToInt32 (8),
                Method = header.ToUInt16 (0xC),
            };
            // 0x14 -> 32-bit checksum after encryption
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GbpReader (file, (GbpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GbpFormat.Write not implemented");
        }
    }

    internal class GbpReader
    {
        IBinaryStream   m_input;
        GbpMetaData     m_info;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_channels;

        public PixelFormat Format { get; private set; }

        public GbpReader (IBinaryStream input, GbpMetaData info)
        {
            m_input = input;
            m_info = info;
            if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr32;

            m_width = (int)m_info.Width;
            m_height = (int)m_info.Height;
            m_output = new byte[4 * m_width * m_height];
            m_channels = m_info.BPP / 8;
            bits_pos = new int[m_channels+1];
            data_pos = new int[m_channels+1];
        }

        public byte[] Unpack ()
        {
            ReadOffsetsTable();
            if (3 == m_info.Method)
                UnpackBlocks();
            else
                UnpackFlat();
            return m_output;
        }

        byte[]  m_frame = new byte[0x1000];
        int[]   bits_pos;
        int[]   data_pos;

        void ReadOffsetsTable ()
        {
            m_input.Position = m_info.HeaderSize;
            bits_pos[0] = m_info.HeaderSize + 4 * m_channels;
            for (int i = 0; i < m_channels; ++i)
            {
                bits_pos[i+1] = bits_pos[i] + m_input.ReadInt32();
            }
            m_input.Position = m_info.DataOffset;
            data_pos[0] = m_info.DataOffset + 4 * m_channels;
            for (int i = 0; i < m_channels; ++i)
            {
                data_pos[i+1] = data_pos[i] + m_input.ReadInt32();
            }
        }

        void UnpackFlat ()
        {
            var channel = new byte[m_width * m_height];
            for (int i = 0; i < 3; ++i)
            {
                m_input.Position = bits_pos[i];
                var bits = m_input.ReadBytes (bits_pos[i+1] - bits_pos[i]);
                m_input.Position = data_pos[i];
                if (1 == m_info.Method)
                {
                    int bits_src = 0;
                    int bit_mask = 0x80;
                    int cdst = 0;
                    while (cdst < channel.Length)
                    {
                        if (0 == bit_mask)
                        {
                            ++bits_src;
                            bit_mask = 0x80;
                        }
                        if ((bits[bits_src] & bit_mask) != 0)
                        {
                            int offset = m_input.ReadUInt16();
                            int count = (offset & 0xF) + 3;
                            offset = (offset >> 4) + 1;
                            Binary.CopyOverlapped (channel, cdst - offset, cdst, count);
                            cdst += count;
                        }
                        else
                        {
                            channel[cdst++] = m_input.ReadUInt8();
                        }
                        bit_mask >>= 1;
                    }
                }
                else if (2 == m_info.Method)
                {
                    LzssUnpack (bits, 0, channel, channel.Length);
                }
                int dst = i;
                byte accum = 0;
                for (int csrc = 0; csrc < channel.Length; ++csrc)
                {
                    accum += channel[csrc];
                    m_output[dst] = accum;
                    dst += 4;
                }
            }
            if (4 == m_channels)
            {
                m_input.Position = data_pos[3];
                int dst = 3;
                while (dst < m_output.Length)
                {
                    byte a = m_input.ReadUInt8();
                    int count = 1;
                    if (a == 0 || a == 0xFF)
                    {
                        count += m_input.ReadUInt8();
                    }
                    while (count --> 0)
                    {
                        m_output[dst] = a;
                        dst += 4;
                    }
                }
            }
        }

        void UnpackBlocks ()
        {
            var channel = new byte[m_width * m_height];
            int stride = m_width * 4;
            int block_stride = stride * 8;
            for (int i = 0; i < m_channels; ++i)
            {
                m_input.Position = bits_pos[i];
                int block_bits_length = m_input.ReadInt32();
                int block_data_length = m_input.ReadInt32();
                int chunk_count = m_input.ReadInt32();
                var bits = m_input.ReadBytes (bits_pos[i+1] - bits_pos[i] - 12);
                int bits_src = block_bits_length + block_data_length;

                m_input.Position = data_pos[i];
                LzssUnpack (bits, bits_src, channel, chunk_count);

                int csrc = 0;
                bits_src = 0;
                int block_src = block_bits_length;
                int dst_block = i;
                int bit_mask = 0x80;
                for (int y = 0; y < m_height; y += 8)
                {
                    int block_height = Math.Min (8, m_height - y);
                    int dst_block_x = dst_block;
                    for (int x = 0; x < m_width; x += 8)
                    {
                        int block_width = Math.Min (8, m_width - x);
                        if (0 == bit_mask)
                        {
                            bit_mask = 0x80;
                            ++bits_src;
                        }
                        int dst_row = dst_block_x;
                        if ((bit_mask & bits[bits_src]) != 0)
                        {
                            byte b = bits[block_src++];
                            for (int by = 0; by < block_height; ++by)
                            {
                                int dst = dst_row;
                                for (int bx = 0; bx < block_width; ++bx)
                                {
                                    m_output[dst] = b;
                                    dst += 4;
                                }
                                dst_row += stride;
                            }
                        }
                        else
                        {
                            for (int by = 0; by < block_height; ++by)
                            {
                                int dst = dst_row;
                                for (int bx = 0; bx < block_width; ++bx)
                                {
                                    m_output[dst] = channel[csrc++];
                                    dst += 4;
                                }
                                dst_row += stride;
                            }
                        }
                        dst_block_x += 32;
                        bit_mask >>= 1;
                    }
                    dst_block += block_stride;
                }
            }
        }

        void LzssUnpack (byte[] ctl_bits, int bits_src, byte[] output, int output_length)
        {
            for (int j = 0; j < m_frame.Length; ++j)
                m_frame[j] = 0;
            int dst = 0;
            int bit_mask = 0x80;
            int frame_pos = 0xFEE;
            while (dst < output_length)
            {
                if (0 == bit_mask)
                {
                    bit_mask = 0x80;
                    ++bits_src;
                }
                if ((bit_mask & ctl_bits[bits_src]) != 0)
                {
                    int offset = m_input.ReadUInt16();
                    int count = (offset & 0xF) + 3;
                    offset >>= 4;
                    while (count --> 0)
                    {
                        byte v = m_frame[offset++ & 0xFFF];
                        output[dst++] = m_frame[frame_pos++ & 0xFFF] = v;
                    }
                }
                else
                {
                    output[dst++] = m_frame[frame_pos++ & 0xFFF] = m_input.ReadUInt8();
                }
                bit_mask >>= 1;
            }
        }
    }
}
