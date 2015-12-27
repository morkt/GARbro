//! \file       ImageBGI.cs
//! \date       Fri Apr 03 01:39:41 2015
//! \brief      BGI/Ethornell engine image format.
//
// Copyright (C) 2015 by morkt
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
using System.Collections.Generic;

namespace GameRes.Formats.BGI
{
    [Export(typeof(ImageFormat))]
    public class BgiFormat : ImageFormat
    {
        public override string         Tag { get { return "BGI"; } }
        public override string Description { get { return "BGI/Ethornell image format"; } }
        public override uint     Signature { get { return 0; } }

        public BgiFormat ()
        {
            Extensions = new string[] { "", "bgi" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                int width  = input.ReadInt16();
                int height = input.ReadInt16();
                if (width <= 0 || height <= 0)
                    return null;
                int bpp = input.ReadInt32();
                if (24 != bpp && 32 != bpp && 8 != bpp)
                    return null;
                if (0 != input.ReadInt64())
                    return null;
                return new ImageMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = bpp,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Gray8;
            int stride = (int)info.Width*((info.BPP+7)/8);
            var pixels = new byte[stride*info.Height];
            stream.Position = 0x10;
            int read = stream.Read (pixels, 0, pixels.Length);
            if (read != pixels.Length)
                throw new InvalidFormatException();
            return ImageData.Create (info, format, null, pixels, stride);
        }
    }

    internal class CbgMetaData : ImageMetaData
    {
        public int  IntermediateLength;
        public uint Key;
        public int  EncLength;
        public byte CheckSum;
        public byte CheckXor;
        public int  Version;
    }

    [Export(typeof(ImageFormat))]
    public class CompressedBGFormat : ImageFormat
    {
        public override string         Tag { get { return "CompressedBG"; } }
        public override string Description { get { return "BGI/Ethornell compressed image format"; } }
        public override uint     Signature { get { return 0x706D6F43; } }

        public CompressedBGFormat ()
        {
            Extensions = new string[] { "", "bgi" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x30];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, "CompressedBG___"))
                return null;
            return new CbgMetaData
            {
                Width  = LittleEndian.ToUInt16 (header, 0x10),
                Height = LittleEndian.ToUInt16 (header, 0x12),
                BPP = LittleEndian.ToInt32 (header, 0x14),
                IntermediateLength = LittleEndian.ToInt32 (header, 0x20),
                Key = LittleEndian.ToUInt32 (header, 0x24),
                EncLength = LittleEndian.ToInt32 (header, 0x28),
                CheckSum = header[0x2C],
                CheckXor = header[0x2D],
                Version = LittleEndian.ToUInt16 (header, 0x2E),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as CbgMetaData;
            if (null == meta)
                throw new ArgumentException ("CompressedBGFormat.Read should be supplied with CbgMetaData", "info");
            using (var reader = new CbgReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (meta, reader.Format, null, reader.Data, reader.Stride);
            }
        }
    }

    internal class CbgReader : BgiDecoderBase
    {
        byte[]          m_output;
        CbgMetaData     m_info;
        int             m_pixel_size;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public CbgReader (Stream input, CbgMetaData info) : base (input, true)
        {
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            Stride = (int)info.Width * m_pixel_size;
            m_key = m_info.Key;
            m_magic = 0;
            switch (m_info.BPP)
            {
            case 32: Format = PixelFormats.Bgra32; break;
            case 24: Format = PixelFormats.Bgr24; break;
            case 8:  Format = PixelFormats.Gray8; break;
            case 16:
                if (2 == m_info.Version)
                    throw new InvalidFormatException();
                Format = PixelFormats.Bgr565;
                break;
            default: throw new InvalidFormatException();
            }
        }

        public void Unpack ()
        {
            Input.Position = 0x30;
            if (m_info.Version < 2)
                UnpackV1();
            else if (2 == m_info.Version)
                UnpackV2();
            else
                throw new NotSupportedException ("Not supported CompressedBG version");
        }

        protected byte[] ReadEncoded ()
        {
            var data = new byte[m_info.EncLength];
            if (data.Length != Input.Read (data, 0, data.Length))
                throw new EndOfStreamException();
            byte sum = 0;
            byte xor = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] -= UpdateKey();
                sum += data[i];
                xor ^= data[i];
            }
            if (sum != m_info.CheckSum || xor != m_info.CheckXor)
                throw new InvalidFormatException ("Compressed stream failed checksum check");
            return data;
        }

        static protected int ReadInteger (Stream input)
        {
            int v = 0;
            int code;
            int code_length = 0;
            do
            {
                code = input.ReadByte();
                if (-1 == code || code_length >= 32)
                    return -1;
                v |= (code & 0x7f) << code_length;
                code_length += 7;
            }
            while (0 != (code & 0x80));
            return v;
        }

        static protected uint[] ReadWeightTable (Stream input, int length)
        {
            uint[] leaf_nodes_weight = new uint[length];
            for (int i = 0; i < length; ++i)
            {
                int weight = ReadInteger (input);
                if (-1 == weight)
                    throw new InvalidFormatException ("Invalid compressed stream");
                leaf_nodes_weight[i] = (uint)weight;
            }
            return leaf_nodes_weight;
        }

        void UnpackV1 ()
        {
            uint[] leaf_nodes_weight;
            using (var enc = new MemoryStream (ReadEncoded()))
                leaf_nodes_weight = ReadWeightTable (enc, 0x100);
            var tree = CreateHuffmanTree (leaf_nodes_weight);
            byte[] packed = new byte[m_info.IntermediateLength];

            HuffmanDecompress (tree, packed);
            m_output = new byte[Stride * (int)m_info.Height];
            UnpackZeros (packed);
            ReverseAverageSampling();
        }

        void HuffmanDecompress (HuffmanNode[] tree, byte[] output)
        {
            for (int dst = 0; dst < output.Length; dst++)
            {
                output[dst] = (byte)DecodeToken (tree);
            }
        }

        void UnpackZeros (byte[] input)
        {
            int dst = 0;
            int dec_zero = 0;
            int src = 0;
            while (dst < m_output.Length)
            {
                int code_length = 0;
                int count = 0;
                byte code;
                do
                {
                    if (src >= input.Length)
                        return;

                    code = input[src++];
                    count |= (code & 0x7f) << code_length;
                    code_length += 7;
                }
                while (0 != (code & 0x80));

                if (dst + count > m_output.Length)
                    break;

                if (0 == dec_zero)
                {
                    if (src + count > input.Length)
                        break;
                    Buffer.BlockCopy (input, src, m_output, dst, count);
                    src += count;
                }
                else
                {
                    for (int i = 0; i < count; ++i)
                        m_output[dst+i] = 0;
                }
                dec_zero ^= 1;
                dst += count;
            }
        }

        void ReverseAverageSampling ()
        {
            for (int y = 0; y < m_info.Height; ++y)
            {
                int line = y * Stride;
                for (int x = 0; x < m_info.Width; ++x)
                {
                    int pixel = line + x * m_pixel_size;
                    for (int p = 0; p < m_pixel_size; p++)
                    {
                        int avg = 0;
                        if (x > 0)
                            avg += m_output[pixel + p - m_pixel_size];
                        if (y > 0)
                            avg += m_output[pixel + p - Stride];
                        if (x > 0 && y > 0)
                            avg /= 2;
                        if (0 != avg)
                            m_output[pixel + p] += (byte)avg;
                    }
                }
            }
        }

        class HuffmanNode
        {
            public bool Valid;
            public bool IsParent;
            public uint Weight;
            public int  LeftChildIndex;
            public int  RightChildIndex;
        }

        static HuffmanNode[] CreateHuffmanTree (uint[] leaf_nodes_weight, bool v2 = false)
        {
            var node_list = new List<HuffmanNode> (leaf_nodes_weight.Length * 2);
            uint root_node_weight = 0;
            for (int i = 0; i < leaf_nodes_weight.Length; ++i)
            {
                var node = new HuffmanNode
                {
                    Valid = leaf_nodes_weight[i] != 0,
                    Weight = leaf_nodes_weight[i],
                    IsParent = false
                };
                node_list.Add (node);
                root_node_weight += node.Weight;
            }
            int[] child_node_index = new int[2];
            for (;;)
            {
                uint weight = 0;
                for (int i = 0; i < 2; i++)
                {
                    uint min_weight = uint.MaxValue;
                    child_node_index[i] = -1;
                    int n = 0;
                    if (v2)
                    {
                        for (; n < node_list.Count; ++n)
                        {
                            if (node_list[n].Valid)
                            {
                                min_weight = node_list[n].Weight;
                                child_node_index[i] = n++;
                                break;
                            }
                        }
                        n = Math.Max (n, i+1);
                    }
                    for (; n < node_list.Count; ++n)
                    {
                        if (node_list[n].Valid && node_list[n].Weight < min_weight)
                        {
                            min_weight = node_list[n].Weight;
                            child_node_index[i] = n;
                        }
                    }
                    if (-1 == child_node_index[i])
                        continue;
                    node_list[child_node_index[i]].Valid = false;
                    weight += node_list[child_node_index[i]].Weight;
                }
                var parent_node = new HuffmanNode
                {
                    Valid = true,
                    IsParent = true,
                    LeftChildIndex  = child_node_index[0],
                    RightChildIndex = child_node_index[1],
                    Weight = weight,
                };
                node_list.Add (parent_node);
                if (weight >= root_node_weight)
                    break;
            }
            return node_list.ToArray();
        }

        int DecodeToken (HuffmanNode[] tree)
        {
            int node_index = tree.Length-1;
            do
            {
                int bit = GetNextBit();
                if (-1 == bit)
                    throw new EndOfStreamException();
                if (0 == bit)
                    node_index = tree[node_index].LeftChildIndex;
                else
                    node_index = tree[node_index].RightChildIndex;
            }
            while (tree[node_index].IsParent);
            return node_index;
        }

        static readonly float[] DCT_Table = {
            1.00000000f, 1.38703990f, 1.30656302f, 1.17587554f, 1.00000000f, 0.78569496f, 0.54119611f, 0.27589938f,
            1.38703990f, 1.92387950f, 1.81225491f, 1.63098633f, 1.38703990f, 1.08979023f, 0.75066054f, 0.38268343f,
            1.30656302f, 1.81225491f, 1.70710683f, 1.53635550f, 1.30656302f, 1.02655995f, 0.70710677f, 0.36047992f,
            1.17587554f, 1.63098633f, 1.53635550f, 1.38268340f, 1.17587554f, 0.92387950f, 0.63637930f, 0.32442334f,
            1.00000000f, 1.38703990f, 1.30656302f, 1.17587554f, 1.00000000f, 0.78569496f, 0.54119611f, 0.27589938f,
            0.78569496f, 1.08979023f, 1.02655995f, 0.92387950f, 0.78569496f, 0.61731654f, 0.42521504f, 0.21677275f,
            0.54119611f, 0.75066054f, 0.70710677f, 0.63637930f, 0.54119611f, 0.42521504f, 0.29289323f, 0.14931567f,
            0.27589938f, 0.38268343f, 0.36047992f, 0.32442334f, 0.27589938f, 0.21677275f, 0.14931567f, 0.07612047f,
        };

        void UnpackV2 ()
        {
            if (m_info.EncLength < 0x80)
                throw new InvalidFormatException();
            var dct_data = ReadEncoded();
            var base_offset = Input.Position;
            var dct = new float[2,64];
            for (int i = 0; i < 0x80; ++i)
            {
                dct[i >> 6, i & 0x3F] = dct_data[i] * DCT_Table[i & 0x3F];
            }
            var tree1 = CreateHuffmanTree (ReadWeightTable (Input, 0x10), true);
            var tree2 = CreateHuffmanTree (ReadWeightTable (Input, 0xB0), true);
            int aligned_width  = ((int)m_info.Width  + 7) & -8;
            int aligned_height = ((int)m_info.Height + 7) & -8;

            m_output = new byte[aligned_width * aligned_height * 4];
            Stride = aligned_width * 4;
            
            int y_blocks = aligned_height / 8;
            var offsets = new uint[y_blocks+1];
            using (var reader = new ArcView.Reader (Input))
            {
                int pad_skip = ((aligned_width >> 3) + 7) >> 3;
                for (int i = 0; i < offsets.Length; ++i)
                    offsets[i] = reader.ReadUInt32();

                int dst = 0;
                for (int i = 0; i < y_blocks; ++i)
                {
                    Reset();
                    Input.Position = base_offset + offsets[i] + pad_skip;
                    int block_size = ReadInteger (Input);
                    if (-1 == block_size)
                        throw new EndOfStreamException();
                    long input_end = i+1 == y_blocks ? Input.Length : (base_offset + offsets[i+1]);
                    var data = UnpackBlock (input_end, block_size, tree1, tree2);
                    if (8 == m_info.BPP)
                        DecodeGrayscale (data, dct, aligned_width, dst);
                    else
                        DecodeRGB (data, dct, aligned_width, dst);
                    dst += aligned_width * 32;
                }
                bool has_alpha = false;
                if (32 == m_info.BPP)
                {
                    Input.Position = base_offset + offsets[y_blocks];
                    has_alpha = UnpackAlpha (reader, aligned_width);
                }
                Format = has_alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            }
        }

        short[] UnpackBlock (long input_end, int block_size, HuffmanNode[] tree1, HuffmanNode[] tree2)
        {
            var color_data = new short[block_size];
            int acc = 0;
            for (int i = 0; i < block_size && Input.Position < input_end; i += 64)
            {
                int count = DecodeToken (tree1);
                if (count != 0)
                {
                    int v = GetBits (count);
                    if (0 == (v >> (count - 1)))
                        v = (-1 << count | v) + 1;
                    acc += v;
                }
                color_data[i] = (short)acc;
            }

            if (0 != (CacheSize & 7))
                GetBits (CacheSize & 7);

            for (int i = 0; i < block_size && Input.Position < input_end; i += 64)
            {
                int index = 1;
                while (index < 64)
                {
                    int code = DecodeToken (tree2);
                    if (0 == code)
                        break;
                    if (0xF == code)
                    {
                        index += 0x10;
                        continue;
                    }
                    index += code & 0xF;
                    if (index >= block_fill_order.Length)
                        break;
                    code >>= 4;
                    int v = GetBits (code);
                    if (code != 0 && 0 == (v >> (code - 1)))
                        v = (-1 << code | v) + 1;
                    color_data[i + block_fill_order[index]] = (short)v;
                    ++index;
                }
            }
            return color_data;
        }

        static readonly byte[] block_fill_order =
        {
            0,  1,  8,  16, 9,  2,  3,  10, 17, 24, 32, 25, 18, 11, 4,  5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6,  7,  14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        };

        short[,] YCbCr_block = new short[64,3];

        void DecodeRGB (short[] data, float[,] dct, int width, int dst)
        {
            int block_count = width / 8;
            for (int i = 0; i < block_count; ++i)
            {
                int src = i * 64;
                for (int channel = 0; channel < 3; ++channel)
                {
                    DecodeDCT (channel, data, src, dct);
                    src += width * 8;
                }
                for (int j = 0; j < 64; ++j)
                {
                    float cy = YCbCr_block[j,0];
                    float cb = YCbCr_block[j,1];
                    float cr = YCbCr_block[j,2];

                    // Full-range YCbCr->RGB conversion
                    //
                    // | 1.0  0.0      1.402   |   | Y        |
                    // | 1.0 -0.34414 -0.71414 | x | Cb - 128 |
                    // | 1.0  1.772    0.0     |   | Cr - 128 |
                    var r = cy + 1.402f * cr - 178.956f;
                    var g = cy - 0.34414f * cb - 0.71414f * cr + 135.95984f;
                    var b = cy + 1.772f * cb - 226.316f;

                    int y = j >> 3;
                    int x = j & 7;
                    int p = (y * width + x) * 4;
                    m_output[dst+p]   = FloatToByte (b);
                    m_output[dst+p+1] = FloatToByte (g);
                    m_output[dst+p+2] = FloatToByte (r);
                }
                dst += 32;
            }
        }

        void DecodeGrayscale (short[] data, float[,] dct, int width, int dst)
        {
            int src = 0;
            int block_count = width / 8;
            for (int i = 0; i < block_count; ++i)
            {
                DecodeDCT (0, data, src, dct);
                src += 64;
                for (int j = 0; j < 64; ++j)
                {
                    int y = j >> 3;
                    int x = j & 7;
                    int p = (y * width + x) * 4;
                    m_output[dst+p]   = (byte)YCbCr_block[j,0];
                    m_output[dst+p+1] = (byte)YCbCr_block[j,0];
                    m_output[dst+p+2] = (byte)YCbCr_block[j,0];
                }
                dst += 32;
            }
        }

        bool UnpackAlpha (BinaryReader input, int width)
        {
            if (1 != input.ReadInt32())
                return false;
            int dst = 3;
            int ctl = 1 << 1;
            while (dst < m_output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                    ctl = input.ReadByte() | 0x100;

                if (0 != (ctl & 1))
                {
                    int v = input.ReadUInt16();
                    int x = v & 0x3F;
                    if (x > 0x1F)
                        x |= -0x40;
                    int y = (v >> 6) & 7;
                    if (y != 0)
                        y |= -8;
                    int count = ((v >> 9) & 0x7F) + 3;

                    int src = dst + (x + y * width) * 4;
                    if (src < 0 || src >= m_output.Length)
                        return false;

                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst] = m_output[src];
                        src += 4;
                        dst += 4;
                    }
                }
                else
                {
                    m_output[dst] = input.ReadByte();
                    dst += 4;
                }
            }
            return true;
        }

        float[,] tmp = new float[8,8];

        void DecodeDCT (int channel, short[] data, int src, float[,] dct_table)
        {
            float v1, v2, v3, v4, v5, v6, v7, v8;
            float v9, v10, v11, v12, v13, v14, v15, v16, v17;
            int d = channel > 0 ? 1 : 0;

            for (int i = 0; i < 8; ++i)
            {
                if (0 == data[src + 8 + i] && 0 == data[src + 16 + i] && 0 == data[src + 24 + i]
                    && 0 == data[src + 32 + i] && 0 == data[src + 40 + i] && 0 == data[src + 48 + i]
                    && 0 == data[src + 56 + i])
                {
                    var t = data[src + i] * dct_table[d, i];
                    tmp[0,i] = t;
                    tmp[1,i] = t;
                    tmp[2,i] = t;
                    tmp[3,i] = t;
                    tmp[4,i] = t;
                    tmp[5,i] = t;
                    tmp[6,i] = t;
                    tmp[7,i] = t;
                    continue;
                }

                v1 = data[src + i] * dct_table[d,i];
                v2 = data[src + 8 + i]  * dct_table[d, 8 + i];
                v3 = data[src + 16 + i] * dct_table[d, 16 + i];
                v4 = data[src + 24 + i] * dct_table[d, 24 + i];
                v5 = data[src + 32 + i] * dct_table[d, 32 + i];
                v6 = data[src + 40 + i] * dct_table[d, 40 + i];
                v7 = data[src + 48 + i] * dct_table[d, 48 + i];
                v8 = data[src + 56 + i] * dct_table[d, 56 + i];

                v10 = v1 + v5;
                v11 = v1 - v5;
                v12 = v3 + v7;
                v13 = (v3 - v7) * 1.414213562f - v12;
                v1 = v10 + v12;
                v7 = v10 - v12;
                v3 = v11 + v13;
                v5 = v11 - v13;
                v14 = v2 + v8;
                v15 = v2 - v8;
                v16 = v6 + v4;
                v17 = v6 - v4;
                v8 = v14 + v16;
                v11 = (v14 - v16) * 1.414213562f;
                v9 = (v17 + v15) * 1.847759065f;
                v10 =  1.082392200f * v15 - v9;
                v13 = -2.613125930f * v17 + v9;
                v6 = v13 - v8;
                v4 = v11 - v6;
                v2 = v10 + v4;

                tmp[0,i] = v1 + v8;
                tmp[1,i] = v3 + v6;
                tmp[2,i] = v5 + v4;
                tmp[3,i] = v7 - v2;
                tmp[4,i] = v7 + v2;
                tmp[5,i] = v5 - v4;
                tmp[6,i] = v3 - v6;
                tmp[7,i] = v1 - v8;
            }
            int dst = 0;
            for (int i = 0; i < 8; ++i)
            {
                v10 = tmp[i,0] + tmp[i,4];
                v11 = tmp[i,0] - tmp[i,4];
                v12 = tmp[i,2] + tmp[i,6];
                v13 = tmp[i,2] - tmp[i,6];
                v14 = tmp[i,1] + tmp[i,7];
                v15 = tmp[i,1] - tmp[i,7];
                v16 = tmp[i,5] + tmp[i,3];
                v17 = tmp[i,5] - tmp[i,3];

                v13 = 1.414213562f * v13 - v12;
                v1 = v10 + v12;
                v7 = v10 - v12;
                v3 = v11 + v13;
                v5 = v11 - v13;
                v8 = v14 + v16;
                v11 = (v14 - v16) * 1.414213562f;
                v9 = (v17 + v15) * 1.847759065f;
                v10 = v9 - v15 * 1.082392200f;
                v13 = v9 - v17 * 2.613125930f;
                v6 = v13 - v8;
                v4 = v11 - v6;
                v2 = v10 - v4;

                YCbCr_block[dst++, channel] = FloatToShort (v1 + v8);
                YCbCr_block[dst++, channel] = FloatToShort (v3 + v6);
                YCbCr_block[dst++, channel] = FloatToShort (v5 + v4);
                YCbCr_block[dst++, channel] = FloatToShort (v7 + v2);
                YCbCr_block[dst++, channel] = FloatToShort (v7 - v2);
                YCbCr_block[dst++, channel] = FloatToShort (v5 - v4);
                YCbCr_block[dst++, channel] = FloatToShort (v3 - v6);
                YCbCr_block[dst++, channel] = FloatToShort (v1 - v8);
            }
        }

        static short FloatToShort (float f)
        {
            int a = 0x80 + (((int)f) >> 3);
            if (a <= 0)
                return 0;
            if (a <= 0xFF)
                return (short)a;
            if (a < 0x180)
                return 0xFF;
            return 0;
        }

        static byte FloatToByte (float f)
        {
            if (f >= 0xFF)
                return 0xFF;
            if (f <= 0)
                return 0;
            return (byte)f;
        }
    }
}
