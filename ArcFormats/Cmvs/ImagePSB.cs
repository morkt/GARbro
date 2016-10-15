//! \file       ImagePSB.cs
//! \date       Thu Jun 23 20:16:31 2016
//! \brief      PVNS engine image format.
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

namespace GameRes.Formats.Pvns
{
    internal class PsbMetaData : ImageMetaData
    {
        public int  Method;
        public int  TableOffset;
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PsbFormat : ImageFormat
    {
        public override string         Tag { get { return "PSB"; } }
        public override string Description { get { return "PVNS engine image format"; } }
        public override uint     Signature { get { return 0x50425350; } } // 'PSBP'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            stream.Seek (-0x13, SeekOrigin.End);
            var tail = stream.ReadBytes (0x13);
            for (int i = 4; i < 0x14; ++i)
            {
                header[i] ^= tail[tail.Length - 3 + (i & 1)];
                header[i] -= tail[i-4];
            }
            return new PsbMetaData
            {
                Width   = header.ToUInt16 (0x0E),
                Height  = header.ToUInt16 (0x10),
                BPP     = header.ToUInt16 (0x12),
                Method  = header.ToUInt16 (0x0C),
                TableOffset = header.ToInt32 (4),
                DataOffset  = header.ToInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new PsbReader (stream.AsStream, (PsbMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PsbFormat.Write not implemented");
        }
    }

    internal sealed class PsbReader
    {
        byte[]              m_input;
        byte[]              m_output;
        PsbMetaData         m_info;
        int                 m_width;
        int                 m_height;
        int                 m_channels;
        byte[]              m_lzss_frame;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public PsbReader (Stream input, PsbMetaData info)
        {
            m_info = info;
            m_width = (int)m_info.Width;
            m_height = (int)m_info.Height;
            m_channels = m_info.BPP / 8;
            if (4 == m_channels)
                Format = PixelFormats.Bgra32;
            else if (3 == m_channels)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
            m_input = new byte[input.Length];
            input.Read (m_input, 0, m_input.Length);
            m_output = new byte[info.Width * info.Height * m_channels];
            m_lzss_frame = new byte[0x1000];
        }

        public void Unpack ()
        {
            switch (m_info.Method)
            {
            case 2: UnpackV2(); break;
            case 3: UnpackV3(); break;
            default:
                throw new NotImplementedException (string.Format ("PSB images type {0} not implemented", m_info.Method));
            }
        }

        void UnpackV2 ()
        {
            int plane_size = m_width * m_height;
            var plane = new byte[plane_size];

            int src = m_info.TableOffset;
            var bits_table = new int[m_channels];
            int offset = m_info.TableOffset + 4 * m_channels;
            for (int i = 0; i < m_channels; ++i)
            {
                bits_table[i] = offset;
                offset += LittleEndian.ToInt32 (m_input, src);
                src += 4;
            }
            src = offset;
            offset += 4 * m_channels;
            var data_table = new int[m_channels];
            for (int i = 0; i < m_channels; ++i)
            {
                data_table[i] = offset;
                offset += LittleEndian.ToInt32 (m_input, src);
                src += 4;
            }
            for (int channel = 0; channel < m_channels; ++channel)
            {
                LzssUnpack (bits_table[channel], data_table[channel], plane, plane.Length);
                int dst = channel;
                byte pixel = 0;
                for (int i = 0; i < plane_size; ++i)
                {
                    pixel += plane[i];
                    m_output[dst] = pixel;
                    dst += m_channels;
                }
            }
        }

        void UnpackV3 ()
        {
            int stride = m_width * m_channels;
            var plane = new byte[m_width * m_height];
            int y_blocks = m_height >> 3;
            int x_blocks = m_width >> 3;
            if (0 != (m_width & 7))
                ++x_blocks;
            if (0 != (m_height & 7))
                ++y_blocks;

            int src = m_info.TableOffset;
            var bits_table = new int[m_channels];
            int offset = m_info.TableOffset + 4 * m_channels;
            for (int i = 0; i < m_channels; ++i)
            {
                bits_table[i] = offset;
                offset += LittleEndian.ToInt32 (m_input, src);
                src += 4;
            }
            src = m_info.DataOffset;
            var data_offsets = new int[m_channels];
            offset = m_info.DataOffset + 4 * m_channels;
            for (int i = 0; i < m_channels; ++i)
            {
                data_offsets[i] = offset;
                offset += LittleEndian.ToInt32 (m_input, src);
                src += 4;
            }

            for (int channel = 0; channel < m_channels; ++channel)
            {
                int dst = channel;

                src = bits_table[channel];
                int bit_length = LittleEndian.ToInt32 (m_input, src);
                int bit_src = src + 12 + bit_length + LittleEndian.ToInt32 (m_input, src+4);
                int plane_size = LittleEndian.ToInt32 (m_input, src+8);

                LzssUnpack (bit_src, data_offsets[channel], plane, plane_size);

                int plane_src = 0;
                bit_src = src + 12;
                src = bit_src + bit_length;
                int bit_mask = 128;
                int y_pos = 0;
                for (int y = 0; y < y_blocks; ++y)
                {
                    int block_height = Math.Min (8, m_height - y_pos);
                    y_pos += 8;
                    int x_pos = 0;
                    int dst_origin = dst;
                    for (int x = 0; x < x_blocks; ++x)
                    {
                        int block_width = Math.Min (8, m_width - x_pos);
                        x_pos += 8;
                        if (0 == bit_mask)
                        {
                            ++bit_src;
                            bit_mask = 128;
                        }
                        if (0 != (bit_mask & m_input[bit_src]))
                        {
                            byte b = m_input[src++];
                            for (int j = 0; j < block_height; ++j)
                            {
                                int d = dst + stride * j;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[d] = b;
                                    d += m_channels;
                                }
                            }
                        }
                        else
                        {
                            for (int j = 0; j < block_height; ++j)
                            {
                                int d = dst + stride * j;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[d] = plane[plane_src++];
                                    d += m_channels;
                                }
                            }
                        }
                        bit_mask >>= 1;
                        dst += 8 * m_channels;
                    }
                    dst = dst_origin + 8 * stride;
                }
            }
        }

        void LzssUnpack (int bit_src, int data_src, byte[] output, int output_size)
        {
            for (int i = 0; i < m_lzss_frame.Length; ++i)
                m_lzss_frame[i] = 0;
            int dst = 0;
            int bit_mask = 0x80;
            int frame_offset = 0xFEE;
            while (dst < output_size)
            {
                if (0 == bit_mask)
                {
                    bit_mask = 0x80;
                    ++bit_src;
                }
                if (0 != (bit_mask & m_input[bit_src]))
                {
                    int v = LittleEndian.ToUInt16 (m_input, data_src);
                    data_src += 2;
                    int count = (v & 0xF) + 3;
                    int offset = v >> 4;
                    for (int i = 0; i < count; ++i)
                    {
                        byte b = m_lzss_frame[(i + offset) & 0xFFF];
                        output[dst++] = b;
                        m_lzss_frame[frame_offset++] = b;
                        frame_offset &= 0xFFF;
                    }
                }
                else
                {
                    byte b = m_input[data_src++];
                    output[dst++] = b;
                    m_lzss_frame[frame_offset++] = b;
                    frame_offset &= 0xFFF;
                }
                bit_mask >>= 1;
            }
        }
    }
}
