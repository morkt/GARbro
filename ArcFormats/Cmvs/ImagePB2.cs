//! \file       ImagePB2.cs
//! \date       Fri Dec 02 23:35:44 2016
//! \brief      Cvns engine image format.
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

namespace GameRes.Formats.Purple
{
    internal class Pb2MetaData : PbMetaData
    {
        public int  Offset1;
        public int  Offset2;
        public int  FrameCount;
    }

    [Export(typeof(ImageFormat))]
    public class Pb2Format : ImageFormat
    {
        public override string         Tag { get { return "PB2"; } }
        public override string Description { get { return "CVNS engine image format"; } }
        public override uint     Signature { get { return 0x41324250; } } // 'PB2A'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            file.Position = file.Length-27;
            var key = file.ReadBytes (27);
            for (int i = 8; i < 0x20; i += 2)
            {
                header[i]   ^= key[24];
                header[i]   -= key[i-8];
                header[i+1] ^= key[25];
                header[i+1] -= key[i-7];
            }
            return new Pb2MetaData
            {
                InputSize = header.ToInt32 (4),
                FrameCount = header.ToInt32 (8),
                Type    = header.ToUInt16 (0x10),
                Width   = header.ToUInt16 (0x12),
                Height  = header.ToUInt16 (0x14),
                BPP     = header.ToUInt16 (0x16),
                Offset1 = header.ToInt32 (0x18),
                Offset2 = header.ToInt32 (0x1C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Pb2Reader (file, (Pb2MetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pb2Format.Write not implemented");
        }
    }

    internal sealed class Pb2Reader : PbReaderBase
    {
        int     m_offset1;
        int     m_offset2;
        int     m_frame_count;

        public Pb2Reader (IBinaryStream input, Pb2MetaData info) : base (info)
        {
            if (32 == info.BPP || 4 == info.Type || 6 == info.Type)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr24;
            m_input = new byte[input.Length];
            input.Read (m_input, 0, m_input.Length);
            if (4 == info.Type || 6 == info.Type)
                m_stride = (int)info.Width * 4;
            else
                m_stride = (int)m_info.Width * info.BPP / 8;
            m_offset1 = info.Offset1;
            m_offset2 = info.Offset2;
            m_frame_count = info.FrameCount;
        }

        public void Unpack ()
        {
            switch (m_info.Type)
            {
            case 1: UnpackV1(); break;
            case 2: UnpackV2(); break;
            case 4: UnpackJbp (0x20, m_offset1); break;
            case 6: UnpackV6(); break;
            case 3:
            case 5:
            case 7:
            default: throw new NotSupportedException(string.Format ("PB2 v{0} images not supported", m_info.Type));
            }
        }

        void UnpackV1 ()
        {
            const int block_size = 8;
            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            int pixel_size = m_info.BPP / 8;

            var block_data = new byte[m_stride * height];
            LzssResetFrame();
            LzssUnpack (m_offset1, m_offset2, block_data, block_data.Length);

            m_output = new byte[block_data.Length];
            int w_block_count = (width + (block_size - 1)) / block_size;
            int h_block_count = (height + (block_size - 1)) / block_size;
            int src = 0;
            for (int c = 0; c < pixel_size; ++c)
            {
                int dst = c;
                for (int block_y = 0; block_y < h_block_count; block_y++)
                {
                    int y = block_y * block_size;
                    int dst_row = dst;
                    int block_height = Math.Min (height - y, block_size);
                    for (int block_x = 0; block_x < w_block_count; block_x++)
                    {
                        int x = block_x * block_size;
                        int dst_pixel = dst_row;
                        int block_width = Math.Min (width - x, block_size);
                        for (int i = 0; i < block_height; i++)
                        {
                            for (int j = 0; j < block_width; j++)
                            {
                                m_output[dst_pixel + j * pixel_size] = block_data[src++];
                            }
                            dst_pixel += m_stride;
                        }
                        dst_row += block_size * pixel_size;
                    }
                    dst += m_stride * block_size;
                }
            }
        }

        void UnpackV2 ()
        {
            m_output = new byte[m_stride * (int)m_info.Height];
            const int block_size = 8;
            byte[] block_data = null;
            int pixel_size = m_info.BPP / 8;
            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            int w_block_count = (width + block_size - 1) / block_size;
            int h_block_count = (height + block_size - 1) / block_size;
            int ctl_offset = m_offset1 + pixel_size * 4;
            int data_offset = m_offset2 + pixel_size * 4;
            for (int c = 0; c < pixel_size; ++c)
            {
                int bit_src = ctl_offset + m_input.ToInt32 (ctl_offset) + m_input.ToInt32 (ctl_offset+4) + 12;
                int unpacked_size = m_input.ToInt32 (ctl_offset+8);
                if (null == block_data || block_data.Length < unpacked_size)
                    block_data = new byte[unpacked_size];
                LzssResetFrame();
                LzssUnpack (bit_src, data_offset, block_data, unpacked_size);

                byte bit_mask = 0x80;
                int block_src = 0;
                bit_src = ctl_offset + 12;
                int src = ctl_offset + m_input.ToInt32 (ctl_offset) + 12;
                int dst_block = c;
                for (int block_y = 0; block_y < h_block_count; block_y++)
                {
                    int y = block_y * block_size;
                    int dst_row = dst_block;
                    int block_height = Math.Min (height - y, block_size);
                    for (int block_x = 0; block_x < w_block_count; block_x++)
                    {
                        int x = block_x * block_size;
                        if (0 == bit_mask)
                        {
                            bit_src++;
                            bit_mask = 0x80;
                        }
                        int dst_pixel = dst_row;
                        int block_width = Math.Min (width - x, block_size);
                        if (0 != (bit_mask & m_input[bit_src]))
                        {
                            byte b = m_input[src++];
                            for (int i = 0 ; i < block_height; i++)
                            {
                                for (int j = 0 ; j < block_width; j++)
                                    m_output[dst_pixel + j * pixel_size] = b;
                                dst_pixel += m_stride;
                            }
                        }
                        else
                        {
                            for (int i = 0 ; i < block_height; i++)
                            {
                                for (int j = 0 ; j < block_width; j++)
                                    m_output[dst_pixel + j * pixel_size] = block_data[block_src++];
                                dst_pixel += m_stride;
                            }
                        }
                        dst_row += block_size * pixel_size;
                        bit_mask >>= 1;
                    }
                    dst_block += m_stride * block_size;
                }
                ctl_offset += m_input.ToInt32 (m_offset1 + c * 4);
                data_offset += m_input.ToInt32 (m_offset2 + c * 4);
            }
        }

        void UnpackV6 ()
        {
            int channel_size = (int)m_info.Width * (int)m_info.Height;
            int src = Array.IndexOf<byte> (m_input, 0, 0x24);
            src = (src + 3) & ~3;
            byte[][] channels = new byte[4][];

            for (int i = 0 ; i < 4; ++i)
            {
                channels[i] = new byte[channel_size];
                int	bit_src  = src + 0x20 + m_input.ToInt32 (src + i * 8);
                int data_src = src + 0x20 + m_input.ToInt32 (src + i * 8 + 4);
                LzssResetFrame();
                LzssUnpack (bit_src, data_src, channels[i], channel_size);
            }
            m_output = new byte[4 * channel_size];
            int dst = 0;
            for (int i = 0; i < channel_size; ++i)
            {
                byte r = (byte)(channels[2][i] ^ channels[3][i]);
                byte g = (byte)(channels[1][i] ^ r);
                byte b = (byte)(channels[0][i] ^ g);
                m_output[dst++] = b;
                m_output[dst++] = g;
                m_output[dst++] = r;
                m_output[dst++] = channels[3][i];
            }
        }
    }
}
