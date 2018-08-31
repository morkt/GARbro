//! \file       ImagePB3.cs
//! \date       Wed Dec 02 13:55:45 2015
//! \brief      Cmvs engine image format.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Linq;
using System.Windows.Media;
using System.Collections.Generic;

namespace GameRes.Formats.Purple
{
    internal class PbMetaData : ImageMetaData
    {
        public int  Type;
        public int  InputSize;
    }

    internal class Pb3MetaData : PbMetaData
    {
        public int  SubType;
    }

    [Export(typeof(ImageFormat))]
    public class Pb3Format : ImageFormat
    {
        public override string         Tag { get { return "PB3"; } }
        public override string Description { get { return "Purple Software image format"; } }
        public override uint     Signature { get { return 0x42334250; } } // 'PB3B'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int input_size = stream.ReadInt32();
            stream.Position = 0x18;
            int t2 = stream.ReadInt32();
            int t1 = stream.ReadUInt16();
            uint width = stream.ReadUInt16();
            uint height = stream.ReadUInt16();
            int bpp = stream.ReadUInt16();
            return new Pb3MetaData
            {
                Width       = width,
                Height      = height,
                BPP         = bpp,
                Type        = t1,
                SubType     = t2,
                InputSize   = input_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new Pb3Reader (stream.AsStream, (Pb3MetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pb3Format.Write not implemented");
        }
    }

    internal class PbReaderBase
    {
        protected PbMetaData    m_info;
        protected byte[]        m_input;
        protected byte[]        m_output;
        protected byte[]        m_lzss_frame = new byte[0x800];
        protected int           m_stride;

        protected PbReaderBase (PbMetaData info)
        {
            m_info = info;
        }

        public PixelFormat Format { get; protected set; }
        public byte[]        Data { get { return m_output; } }

        internal void LzssResetFrame ()
        {
            for (int i = 0; i < 0x7DE; ++i)
                m_lzss_frame[i] = 0;
        }

        internal void LzssUnpack (int bit_src, int data_src, byte[] output, int output_size)
        {
            int dst = 0;
            int bit_mask = 0x80;
            int frame_offset = 0x7DE;
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
                    int count = (v & 0x1F) + 3;
                    int offset = v >> 5;
                    for (int i = 0; i < count; ++i)
                    {
                        byte b = m_lzss_frame[(i + offset) & 0x7FF];
                        output[dst++] = b;
                        m_lzss_frame[frame_offset++] = b;
                        frame_offset &= 0x7FF;
                    }
                }
                else
                {
                    byte b = m_input[data_src++];
                    output[dst++] = b;
                    m_lzss_frame[frame_offset++] = b;
                    frame_offset &= 0x7FF;
                }
                bit_mask >>= 1;
            }
        }

        internal void UnpackJbp (int jbp_pos, int alpha_pos)
        {
            var jbp = new JbpReader (m_input, jbp_pos);
            m_output = jbp.Unpack();
            if (m_stride != jbp.Stride)
            {
                int src_stride = jbp.Stride;
                int src = src_stride;
                int dst = m_stride;
                for (uint y = 1; y < m_info.Height; ++y)
                {
                    Buffer.BlockCopy (m_output, src, m_output, dst, m_stride);
                    src += src_stride;
                    dst += m_stride;
                }
            }
            if (32 == m_info.BPP && alpha_pos > 0)
            {
                int dst = 3;
                int output_end = m_stride * (int)m_info.Height;
                while (dst < output_end)
                {
                    byte alpha = m_input[alpha_pos++];
                    if (0 != alpha && 0xFF != alpha)
                    {
                        m_output[dst] = alpha;
                        dst += 4;
                    } 
                    else
                    {
                        int count = m_input[alpha_pos++];
                        while (count --> 0)
                        {
                            m_output[dst] = alpha;
                            dst += 4;
                        }
                    }
                }
            }
            else
            {
                Format = PixelFormats.Bgr32;
            }
        }
    }

    internal sealed class Pb3Reader : PbReaderBase
    {
        int             m_channels;

        public Pb3Reader (Stream input, Pb3MetaData info) : base (info)
        {
            if (info.Type == 1 && info.SubType != 0x10)
                throw new NotSupportedException();
            if (3 == m_info.Type || 2 == m_info.Type)
                m_input = new byte[input.Length];
            else
                m_input = new byte[m_info.InputSize];
            if (m_input.Length != input.Read (m_input, 0, m_input.Length))
                throw new EndOfStreamException();
            m_channels = m_info.BPP / 8;
            m_stride = 4 * (int)m_info.Width;
            Format = m_channels < 4 ? PixelFormats.Bgr32 : PixelFormats.Bgra32;
            // output array created by unpack methods as needed.
        }

        public void Unpack ()
        {
            switch (m_info.Type)
            {
            default: throw new InvalidEncryptionScheme();
            case 1: UnpackV1(); break;
            case 5: UnpackV5(); break;
            case 8:
            case 6: UnpackV6(); break;
            case 2:
            case 3: UnpackJbp (0x34, m_input.ToInt32 (0x2C)); break;
            case 4:
            case 7: throw new NotSupportedException(string.Format ("PB3 v{0} images not supported", m_info.Type));
            }
        }

        void UnpackV1 ()
        {
            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            m_output = new byte[m_stride * height];

            int x_blocks = width >> 4;
            if (0 != (width & 0xF))
                ++x_blocks;
            int y_blocks = height >> 4;
            if (0 != (height & 0xF))
                ++y_blocks;

            int plane_size = width * height;
            byte[] plane = new byte[plane_size];

            int data1 = LittleEndian.ToInt32 (m_input, 0x2C);
            int data2 = LittleEndian.ToInt32 (m_input, 0x30);

            for (int channel = 0; channel < m_channels; ++channel)
            {
                int channel_offset = 4 * m_channels;
                for (int i = 0; i < channel; ++i)
                    channel_offset += LittleEndian.ToInt32 (m_input, data1 + 4*i);
                int v21 = data1 + channel_offset;
                int bit_src = v21 + 12 + LittleEndian.ToInt32 (m_input, v21) + LittleEndian.ToInt32 (m_input, v21+4);
                int channel_size = LittleEndian.ToInt32 (m_input, v21 + 8);

                channel_offset = 4 * m_channels;
                for (int i = 0; i < channel; ++i)
                    channel_offset += LittleEndian.ToInt32 (m_input, data2 + 4*i);
                int data_src = data2 + channel_offset;

                LzssResetFrame();
                LzssUnpack (bit_src, data_src, plane, channel_size);

                if (0 == y_blocks || 0 == x_blocks)
                    continue;
                int plane_src = 0;
                bit_src = v21 + 12;
                int bit_mask = 128;
                data_src = bit_src + LittleEndian.ToInt32 (m_input, v21);
                int v68 = 16;
                for (int y = 0; y < y_blocks; ++y)
                {
                    int row = 16 * y;
                    int v66 = 16;
                    int dst_origin = m_stride * row + channel; // within m_output
                    for (int x = 0; x < x_blocks; ++x)
                    {
                        int dst = dst_origin;
                        int block_width  = v66 > width  ? width - 16 * x : 16;
                        int block_height = v68 > height ? height - row   : 16;
                        if (0 == bit_mask)
                        {
                            ++bit_src;
                            bit_mask = 128;
                        }
                        if (0 != (bit_mask & m_input[bit_src]))
                        {
                            byte b = m_input[data_src++];
                            for (int j = 0; j < block_height; ++j)
                            {
                                int v49 = dst;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[v49] = b;
                                    v49 += 4;
                                }
                                dst += m_stride;
                            }
                        }
                        else
                        {
                            for (int j = 0; j < block_height; ++j)
                            {
                                int v49 = dst;
                                for (int i = 0; i < block_width; ++i)
                                {
                                    m_output[v49] = plane[plane_src++];
                                    v49 += 4;
                                }
                                dst += m_stride;
                            }
                        }
                        bit_mask >>= 1;
                        v66 += 16;
                        dst_origin += 64;
                    }
                    v68 += 16;
                }
            }
        }

        void UnpackV5 ()
        {
            m_output = new byte[m_stride * (int)m_info.Height];
            for (int i = 0; i < 4; ++i)
            {
                int bit_src  = 0x54 + LittleEndian.ToInt32 (m_input, 8 * i + 0x34);
                int data_src = 0x54 + LittleEndian.ToInt32 (m_input, 8 * i + 0x38);
                LzssResetFrame();
                int frame_offset = 0x7DE;
                byte accum = 0;
                int bit_mask = 128;
                int dst = i;
                while (dst < m_output.Length)
                {
                    if (0 == bit_mask)
                    {
                        ++bit_src;
                        bit_mask = 128;
                    }
                    if (0 != (bit_mask & m_input[bit_src]))
                    {
                        int v = LittleEndian.ToUInt16 (m_input, data_src);
                        data_src += 2;
                        int count = (v & 0x1F) + 3;
                        int offset = v >> 5;
                        for (int k = 0; k < count; ++k)
                        {
                            byte b = m_lzss_frame[(k + offset) & 0x7FF];
                            m_lzss_frame[frame_offset++] = b;
                            accum += b;
                            m_output[dst] = accum;
                            dst += 4;
                            frame_offset &= 0x7FF;
                        }
                    }
                    else
                    {
                        byte b = m_input[data_src++];
                        m_lzss_frame[frame_offset++] = b;
                        accum += b;
                        m_output[dst] = accum;
                        dst += 4;
                        frame_offset &= 0x7FF;
                    }
                    bit_mask >>= 1;
                }
            }
        }

        static readonly byte[] NameKeyV6 = {
            0xA6, 0x75, 0xF3, 0x9C, 0xC5, 0x69, 0x78, 0xA3, 0x3E, 0xA5, 0x4F, 0x79, 0x59, 0xFE, 0x3A, 0xC7,
        };

        void UnpackV6 ()
        {
            var name_bytes = new byte[0x20];
            int name_offset = 0x34;
            int i;
            for (i = 0; i < 0x20; ++i)
            {
                name_bytes[i] = (byte)(m_input[name_offset+i] ^ NameKeyV6[i & 0xF]);
                if (0 == name_bytes[i])
                    break;
            }
            m_output = LoadBaseImage (Encodings.cp932.GetString (name_bytes, 0, i) + ".pb3");
            BlendInput();
        }

        byte[] LoadBaseImage (string name)
        {
            // judging by the code, files with "pb3" extension could as well contain PNG or BMP images,
            // so we couldn't just shortcut to another instance of Pb3Reader here.

            var path = VFS.GetDirectoryName (m_info.FileName);
            name = VFS.CombinePath (path, name);
            if (name.Equals (m_info.FileName, StringComparison.InvariantCultureIgnoreCase))
                throw new InvalidFormatException();
            // two files referencing each other still could create infinite recursion
            using (var base_file = VFS.OpenBinaryStream (name))
            {
                var image_data = ImageFormat.Read (base_file);
                int stride = image_data.Bitmap.PixelWidth * 4;
                var pixels = new byte[stride * image_data.Bitmap.PixelHeight];
                image_data.Bitmap.CopyPixels (pixels, stride, 0);
                return pixels;
            }
        }

        void BlendInput ()
        {
            int bit_src = 0x20 + LittleEndian.ToInt32 (m_input, 0xC);
            int data_src = bit_src + LittleEndian.ToInt32 (m_input, 0x2C);
            int overlay_size = LittleEndian.ToInt32 (m_input, 0x18);
            var overlay = new byte[overlay_size];
            LzssUnpack (bit_src, data_src, overlay, overlay_size);

            int width  = (int)m_info.Width;
            int height = (int)m_info.Height;
            bit_src = 8; // within overlay
            data_src = 8 + LittleEndian.ToInt32 (overlay, 0); // within overlay

            int bit_mask = 0x80;
            int x_blocks = width >> 3;
            if (0 != (width & 7))
                ++x_blocks;
            int y_blocks = height >> 3;
            if (0 != (height & 7))
                ++y_blocks;
            if (0 == x_blocks)
                return;
            int h = 0;
            int dst_origin = 0;
            while (y_blocks > 0)
            {
                int w = 0;
                for (int x = 0; x < x_blocks; ++x)
                {
                    if (0 == bit_mask)
                    {
                        ++bit_src;
                        bit_mask = 0x80;
                    }
                    if (0 == (bit_mask & overlay[bit_src]))
                    {
                        int dst = 8 * (dst_origin + 4 * x); // within m_output
                        int x_count = Math.Min (8, width - w);
                        int y_count = Math.Min (8, height - h);
                        for (int v30 = y_count; v30 > 0; --v30)
                        {
                            int count = 4 * x_count;
                            Buffer.BlockCopy (overlay, data_src, m_output, dst, count);
                            data_src += count;
                            dst += m_stride;
                        }
                    }
                    bit_mask >>= 1;
                    w += 8;
                }
                dst_origin += m_stride;
                h += 8;
                --y_blocks;
            }
        }
    }

    public sealed class JbpReader
    {
        byte[]  m_input;
        byte[]  m_output;
        int     m_data_pos;
        uint    m_format;

        int     m_aligned_width;
        int     m_aligned_height;
        int     m_stride;
        int     m_blocks_x;
        int     m_blocks_y;
        int     m_dc_bits;
        int     m_ac_bits;

        public int  Width { get { return m_aligned_width; } }
        public int Height { get { return m_aligned_height; } }
        public int Stride { get { return m_stride; } }

        public JbpReader (byte[] input, int offset)
        {
            m_input     = input;
            m_data_pos  = LittleEndian.ToInt32 (input, offset+4) + offset;
            m_format    = LittleEndian.ToUInt32 (input, offset+8);
            int width   = LittleEndian.ToUInt16 (input, offset+0x10);
            int height  = LittleEndian.ToUInt16 (input, offset+0x12);
            m_dc_bits = LittleEndian.ToInt32 (input, offset+0x1C);
            m_ac_bits = LittleEndian.ToInt32 (input, offset+0x20);

            switch ((m_format >> 28) & 3)
            {
            case 0:
                m_aligned_width  = (width  + 7) & ~7;
                m_aligned_height = (height + 7) & ~7;
                break;
            case 1:
                m_aligned_width  = (width  + 0xF) & ~0xF;
                m_aligned_height = (height + 0xF) & ~0xF;
                break;
            case 2:
                m_aligned_width  = (width  + 0x1F) & ~0x1F;
                m_aligned_height = (height + 0x0F) & ~0x0F;
                break;
            default:
                throw new InvalidFormatException();
            }
            m_blocks_x = m_aligned_width  >> 4;
            m_blocks_y = m_aligned_height >> 4;

            m_stride = 4 * m_aligned_width;
            m_output = new byte[m_stride * m_aligned_height];
        }

        short[] quant_y = new short[0x40];
        short[] quant_c = new short[0x40];

        JBitStream  bits_dc;
        JBitStream  bits_ac;

        HuffmanTree tree_dc;
        HuffmanTree tree_ac;

        public byte[] Unpack ()
        {
            var tree_data = new byte[0x10];
            int tree_pos = m_data_pos+0x80;
            for (int i = 0; i < 0x10; ++i)
                tree_data[i] = (byte)(m_input[tree_pos+i] + 1);

            var freq = new uint[0x20];
            Buffer.BlockCopy (m_input, m_data_pos, freq, 0, 0x40);
            tree_dc = new HuffmanTree (tree_data, freq);
            Buffer.BlockCopy (m_input, m_data_pos+0x40, freq, 0, 0x40);
            tree_ac = new HuffmanTree (tree_data, freq);

            int quant_pos = tree_pos+0x10;
            if (0 != (m_format & 0x8000000))
            {
                for (int i = 0; i < 0x40; ++i)
                {
                    quant_y[i] = m_input[quant_pos+i];
                    quant_c[i] = m_input[quant_pos+i+0x40];
                }
            }
            int bits_offset = quant_pos + 0x80;
            bits_dc = new JBitStream (m_input, bits_offset,             m_dc_bits);
            bits_ac = new JBitStream (m_input, bits_offset + m_dc_bits, m_ac_bits);

            Decode();
            return m_output;
        }

        static byte[] ZigzagOrder = new byte[64]
        {
             1,  8,  16, 9,  2,  3, 10, 17,
            24, 32, 25, 18, 11,  4,  5, 12,
            19, 26, 33, 40, 48, 41, 34, 27,
            20, 13,  6,  7, 14, 21, 28, 35,
            42, 49, 56, 57, 50, 43, 36, 29,
            22, 15, 23, 30, 37, 44, 51, 58,
            59, 52, 45, 38, 31, 39, 46, 53,
            60, 61, 54, 47, 55, 62, 63,  0
        };

        void Decode ()
        {
            int total_blocks = m_blocks_x * m_blocks_y;
            var blocks = new short[total_blocks, 6];
            uint prev_v = 0;
            for (int i = 0; i < total_blocks; ++i)
            for (int j = 0; j < 6; ++j)
            {
                int bit_count = tree_dc.Read (bits_dc);
                uint v = (uint)bits_dc.GetBits (bit_count);
                if (v < (1u << (bit_count - 1)))
                    v -= (1u << bit_count) - 1;

                prev_v += v;
                blocks[i,j] = (short)prev_v;
            }

            var dct_table = new short[6][];
            for (int i = 0; i < 6; ++i)
                dct_table[i] = new short[64];

            for (int y = 0; y < m_blocks_y; ++y)
            {
                int dst1 = y * m_stride * 16;
                int dst2 = dst1 + m_stride * 9;

                for (int x = 0; x < m_blocks_x; ++x)
                {
                    for (int j = 0; j < 6; ++j)
                    for (int k = 0; k < 64; ++k)
                        dct_table[j][k] = 0;

                    for (int n = 0; n < 6; ++n)
                    {
                        dct_table[n][0] = blocks[y * m_blocks_x + x, n];

                        for (int i = 0; i < 63;)
                        {
                            int bit_count = tree_ac.Read (bits_ac);

                            if (15 == bit_count)
                                break;

                            if (0 == bit_count)
                            {
                                int node_idx = 0;
                                while (0 != bits_ac.GetNextBit())
                                    node_idx++;
                                i += tree_ac.Base[node_idx];
                            }
                            else
                            {
                                uint v = (uint)bits_ac.GetBits (bit_count);
                                if (v < (1u << (bit_count - 1)))
                                    v -= (1u << bit_count) - 1;
                                dct_table[n][ZigzagOrder[i]] = (short)v;
                                ++i;
                            }
                        }
                    }

                    Dct (dct_table[0], quant_y);
                    Dct (dct_table[1], quant_y);
                    Dct (dct_table[2], quant_y);
                    Dct (dct_table[3], quant_y);
                    Dct (dct_table[4], quant_c);
                    Dct (dct_table[5], quant_c);

                    Ycc2Rgb (dst1,             dst1+m_stride,    dct_table[0], dct_table[4], dct_table[5], 0);
                    Ycc2Rgb (dst1+32,          dst1+m_stride+32, dct_table[1], dct_table[4], dct_table[5], 4);
                    Ycc2Rgb (dst2-m_stride,    dst2,             dct_table[2], dct_table[4], dct_table[5], 32);
                    Ycc2Rgb (dst2-m_stride+32, dst2+32,          dct_table[3], dct_table[4], dct_table[5], 36);

                    dst1 += 64;
                    dst2 += 64;
                }
            }
        }

        void Dct (short[] dct_table, short[] quant)
        {
            int a, b, c, d;
            int w, x, y, z;
            int s, t, u, v, n;

            int p = 0;
            int q = 0;

            for (int i = 0; i < 8; ++i)
            {
                if (dct_table[p+0x08] == 0 && dct_table[p+0x10] == 0 &&
                    dct_table[p+0x18] == 0 && dct_table[p+0x20] == 0 &&
                    dct_table[p+0x28] == 0 && dct_table[p+0x30] == 0 &&
                    dct_table[p+0x38] == 0)
                {
                    dct_table[p]      = dct_table[p+0x08] =
                    dct_table[p+0x10] = dct_table[p+0x18] =
                    dct_table[p+0x20] = dct_table[p+0x28] =
                    dct_table[p+0x30] = dct_table[p+0x38] = (short)(dct_table[p] * quant[q]);
                }
                else
                {
                    c = quant[q+0x10] * dct_table[p+0x10];
                    d = quant[q+0x30] * dct_table[p+0x30];
                    x = ((c + d) * 35467) >> 16;
                    c = ((c * 50159) >> 16) + x;
                    d = ((d * -121094) >> 16) + x;
                    a = dct_table[p+0x00] * quant[q+0x00];
                    b = dct_table[p+0x20] * quant[q+0x20];
                    w = a + b + c;
                    x = a + b - c;
                    y = a - b + d;
                    z = a - b - d;

                    c = dct_table[p+0x38] * quant[q+0x38];
                    d = dct_table[p+0x28] * quant[q+0x28];
                    a = dct_table[p+0x18] * quant[q+0x18];
                    b = dct_table[p+0x08] * quant[q+0x08];
                    n = ((a + b + c + d) * 77062) >> 16;

                    u = n + ((c * 19571) >> 16)  + (((c + a) * -128553) >> 16) + (((c + b) * -58980) >> 16);
                    v = n + ((d * 134553) >> 16) + (((d + b) * -25570) >> 16)  + (((d + a) * -167963) >> 16);
                    t = n + ((b * 98390) >> 16)  + (((d + b) * -25570) >> 16)  + (((c + b) * -58980) >> 16);
                    s = n + ((a * 201373) >> 16) + (((c + a) * -128553) >> 16) + (((d + a) * -167963) >> 16);

                    dct_table[p]      = (short)(w + t);
                    dct_table[p+0x38] = (short)(w - t);
                    dct_table[p+0x08] = (short)(y + s);
                    dct_table[p+0x30] = (short)(y - s);
                    dct_table[p+0x10] = (short)(z + v);
                    dct_table[p+0x28] = (short)(z - v);
                    dct_table[p+0x18] = (short)(x + u);
                    dct_table[p+0x20] = (short)(x - u);
                }
                p++;
                q++;
            }

            p = 0;
            for (int i = 0; i < 8; ++i)
            {
                a = dct_table[p];
                c = dct_table[p+2];
                b = dct_table[p+4];
                d = dct_table[p+6];
                x = ((c + d) * 35467) >> 16;
                c = ((c * 50159) >> 16) + x;
                d = ((d * -121094) >> 16) + x;
                w = a + b + c;
                x = a + b - c;
                y = a - b + d;
                z = a - b - d;

                d = dct_table[p+5];
                b = dct_table[p+1];
                c = dct_table[p+7];
                a = dct_table[p+3];
                n = ((a + b + c + d) * 77062) >> 16;

                s = n + ((a * 201373) >> 16) + (((a + c) * -128553) >> 16) + (((a + d) * -167963) >> 16);
                t = n + ((b * 98390) >> 16)  + (((b + c) * -58980) >> 16)  + (((b + d) * -25570) >> 16);
                u = n + ((c * 19571) >> 16)  + (((b + c) * -58980) >> 16)  + (((a + c) * -128553) >> 16);
                v = n + ((d * 134553) >> 16) + (((b + d) * -25570) >> 16)  + (((a + d) * -167963) >> 16);

                dct_table[p  ] = (short)((w + t) >> 3);
                dct_table[p+7] = (short)((w - t) >> 3);
                dct_table[p+1] = (short)((y + s) >> 3);
                dct_table[p+6] = (short)((y - s) >> 3);
                dct_table[p+2] = (short)((z + v) >> 3);
                dct_table[p+5] = (short)((z - v) >> 3);
                dct_table[p+3] = (short)((x + u) >> 3);
                dct_table[p+4] = (short)((x - u) >> 3);

                p += 8;
            }
        }

        void Ycc2Rgb (int dc, int ac, short[] dct_y, short[] dct_cb, short[] dct_cr, int cbcr_src)
        {
            int y_src = 0;
            for (int y = 0; y < 4; ++y)
            {
                for (int x = 0; x < 4; ++x)
                {
                    var cb = dct_cb[cbcr_src];
                    var cr = dct_cr[cbcr_src];
                    var r = ((cr * 0x166F0) >> 16);
                    var g = ((cb * 0x5810) >> 16) + ((cr * 0xB6C0) >> 16);
                    var b = ((cb * 0x1C590) >> 16);
                    var c0 = dct_y[y_src  ] + 0x180;
                    var c1 = dct_y[y_src+1] + 0x180;
                    var c8 = dct_y[y_src+8] + 0x180;
                    var c9 = dct_y[y_src+9] + 0x180;

                    m_output[dc]            = Clamp (c0 + b);
                    m_output[ac+1-m_stride] = Clamp (c0 - g);
                    m_output[ac+2-m_stride] = Clamp (c0 + r);
                    m_output[ac+4-m_stride] = Clamp (c1 + b);
                    m_output[ac+5-m_stride] = Clamp (c1 - g);
                    m_output[ac+6-m_stride] = Clamp (c1 + r);
                    m_output[ac]            = Clamp (c8 + b);
                    m_output[ac+1]          = Clamp (c8 - g);
                    m_output[ac+2]          = Clamp (c8 + r);
                    m_output[ac+4]          = Clamp (c9 + b);
                    m_output[ac+5]          = Clamp (c9 - g);
                    m_output[ac+6]          = Clamp (c9 + r);
                    y_src += 2;
                    dc += 8;
                    ac += 8;
                    cbcr_src++;
                }

                dc += m_stride * 2 - 32;
                ac += m_stride * 2 - 32;

                y_src += 8;
                cbcr_src += 4;
            }
        }

        static byte[] YccClampTable = Enumerable.Repeat<byte> (0, 0x100)
            .Concat (Enumerable.Range (0, 0x100).Select (x => (byte)x))
            .Concat (Enumerable.Repeat<byte> (0xFF, 0x100)).ToArray();

        static byte Clamp (int c)
        {
            return YccClampTable[c];
        }

        internal class HuffmanTree
        {
            public  byte[]  Base;
            private int[]   Nodes = new int[0x400];
            private int     Root;
            public  int     LeafCount { get { return Base.Length; } }

            const uint      MaxFreq = 2100000000u;

            public HuffmanTree (byte[] input, uint[] freq)
            {
                Base = input;
                int depth = Base.Length;
                for (;;)
                {
                    int l = -1;
                    uint min = MaxFreq - 1;
                    for (int i = 0; i < depth; ++i)
                    {
                        if (freq[i] < min)
                        {
                            min = freq[i];
                            l = i;
                        }
                    }

                    int r = -1;
                    min = MaxFreq - 1;
                    for (int i = 0; i < depth; ++i)
                    {
                        if ((i != l) && (freq[i] < min))
                        {
                            min = freq[i];
                            r = i;
                        }
                    }

                    if (l < 0 || r < 0)
                        break;

                    Nodes[depth] = l;
                    Nodes[depth+0x200] = r;

                    freq[depth++] = freq[l] + freq[r];
                    freq[l] = MaxFreq;
                    freq[r] = MaxFreq;
                }
                Root = depth - 1;
            }

            public int Read (JBitStream bits)
            {
                int v = Root;
                while (v >= LeafCount)
                    v = Nodes[v + (bits.GetNextBit() << 9)];
                return v;
            }
        }

        internal class JBitStream
        {
            byte[]      m_input;
            int         m_pos;
            int         m_end;

            public JBitStream (byte[] input, int offset, int length)
            {
                m_input = input;
                m_pos   = offset;
                m_end   = offset + length;
            }

            int         m_bits = 0;
            int         m_cached_bits = 0;

            public int GetBits (int count)
            {
                while (m_cached_bits < count)
                {
                    if (m_pos >= m_end)
                        throw new EndOfStreamException();
                    m_bits = (m_bits << 8) | ReverseByteBits (m_input[m_pos++]);
                    m_cached_bits += 8;
                }
                int mask = (1 << count) - 1;
                m_cached_bits -= count;
                return (m_bits >> m_cached_bits) & mask;
            }

            public int GetNextBit ()
            {
                return GetBits (1);
            }

            int ReverseByteBits (int x)
            {
                x = (x & 0xAA) >> 1 | (x & 0x55) << 1;
                x = (x & 0xCC) >> 2 | (x & 0x33) << 2;
                return (x >> 4 | x << 4) & 0xFF;
            }
        }
    }
}
