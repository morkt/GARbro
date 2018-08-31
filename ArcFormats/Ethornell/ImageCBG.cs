//! \file       ImageCBG.cs
//! \date       2018 Aug 31
//! \brief      BGI/Ethornell engine image format.
//
// Copyright (C) 2015-2018 by morkt
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameRes.Formats.BGI
{
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

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            if (!header.AsciiEqual ("CompressedBG___"))
                return null;
            return new CbgMetaData
            {
                Width  = header.ToUInt16 (0x10),
                Height = header.ToUInt16 (0x12),
                BPP = header.ToInt32 (0x14),
                IntermediateLength = header.ToInt32 (0x20),
                Key = header.ToUInt32 (0x24),
                EncLength = header.ToInt32 (0x28),
                CheckSum = header[0x2C],
                CheckXor = header[0x2D],
                Version = header.ToUInt16 (0x2E),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (CbgMetaData)info as CbgMetaData;
            using (var reader = new CbgReader (stream.AsStream, meta))
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
            {
                if (m_info.EncLength < 0x80)
                    throw new InvalidFormatException();
                using (var decoder = new ParallelCbgDecoder (m_info, ReadEncoded()))
                    UnpackV2 (decoder);
            }
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

        static internal int ReadInteger (Stream input)
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
            var tree = new HuffmanTree (leaf_nodes_weight);
            byte[] packed = new byte[m_info.IntermediateLength];

            HuffmanDecompress (tree, packed);
            m_output = new byte[Stride * (int)m_info.Height];
            UnpackZeros (packed);
            ReverseAverageSampling();
        }

        void HuffmanDecompress (HuffmanTree tree, byte[] output)
        {
            for (int dst = 0; dst < output.Length; dst++)
            {
                output[dst] = (byte)tree.DecodeToken (this);
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

        void UnpackV2 (ParallelCbgDecoder decoder)
        {
            var base_offset = Input.Position;
            decoder.Tree1 = new HuffmanTree (ReadWeightTable (Input, 0x10), true);
            decoder.Tree2 = new HuffmanTree (ReadWeightTable (Input, 0xB0), true);

            int y_blocks = decoder.Height / 8;
            var offsets = new int[y_blocks+1];
            int input_base = (int)(Input.Position + offsets.Length*4 - base_offset);
            using (var reader = new ArcView.Reader (Input))
            {
                for (int i = 0; i < offsets.Length; ++i)
                    offsets[i] = reader.ReadInt32() - input_base;
                decoder.Input = reader.ReadBytes ((int)(Input.Length - Input.Position));
            }
            int pad_skip = ((decoder.Width >> 3) + 7) >> 3;
            var tasks = new List<Task> (y_blocks+1);
            decoder.Output = new byte[decoder.Width * decoder.Height * 4];
            int dst = 0;
            for (int i = 0; i < y_blocks; ++i)
            {
                int block_offset = offsets[i] + pad_skip;
                int next_offset = i+1 == y_blocks ? decoder.Input.Length : offsets[i+1];
                int closure_dst = dst;
                var task = Task.Run (() => decoder.UnpackBlock (block_offset, next_offset-block_offset, closure_dst));
                tasks.Add (task);
                dst += decoder.Width * 32;
            }
            if (32 == m_info.BPP)
            {
                var task = Task.Run (() => decoder.UnpackAlpha (offsets[y_blocks]));
                tasks.Add (task);
            }
            var complete = Task.WhenAll (tasks);
            complete.Wait();
            Format = decoder.HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            Stride = decoder.Width * 4;
            m_output = decoder.Output;
        }
    }

    internal class HuffmanTree
    {
        HuffmanNode[]       m_nodes;

        class HuffmanNode
        {
            public bool Valid;
            public bool IsParent;
            public uint Weight;
            public int  LeftChildIndex;
            public int  RightChildIndex;
        }

        public HuffmanTree (uint[] leaf_nodes_weight, bool v2 = false)
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
            m_nodes = node_list.ToArray();
        }

        public int DecodeToken (IBitStream input)
        {
            int node_index = m_nodes.Length-1;
            do
            {
                int bit = input.GetNextBit();
                if (-1 == bit)
                    throw new EndOfStreamException();
                if (0 == bit)
                    node_index = m_nodes[node_index].LeftChildIndex;
                else
                    node_index = m_nodes[node_index].RightChildIndex;
            }
            while (m_nodes[node_index].IsParent);
            return node_index;
        }
    }

    internal sealed class ParallelCbgDecoder : IDisposable
    {
        public byte[]           Input;
        public byte[]           Output;
        public int              BPP;
        public int              Width;
        public int              Height;
        public HuffmanTree      Tree1;
        public HuffmanTree      Tree2;
        public bool             HasAlpha = false;
        float[,]                DCT = new float[2, 64];

        public ParallelCbgDecoder (CbgMetaData info, byte[] dct_data)
        {
            BPP = info.BPP;
            Width  = ((int)info.Width  + 7) & -8;
            Height = ((int)info.Height + 7) & -8;

            for (int i = 0; i < 0x80; ++i)
            {
                DCT[i >> 6, i & 0x3F] = dct_data[i] * DCT_Table[i & 0x3F];
            }
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

        public void UnpackBlock (int offset, int length, int dst)
        {
            using (var input = new MemoryStream (this.Input, offset, length))
            using (var reader = new MsbBitStream (input))
            {
                int block_size = CbgReader.ReadInteger (input);
                if (-1 == block_size)
                    return;
                var color_data = new short[block_size];
                int acc = 0;
                for (int i = 0; i < block_size && input.Position < input.Length; i += 64)
                {
                    int count = Tree1.DecodeToken (reader);
                    if (count != 0)
                    {
                        int v = reader.GetBits (count);
                        if (0 == (v >> (count - 1)))
                            v = (-1 << count | v) + 1;
                        acc += v;
                    }
                    color_data[i] = (short)acc;
                }

                if (0 != (reader.CacheSize & 7))
                    reader.GetBits (reader.CacheSize & 7);

                for (int i = 0; i < block_size && input.Position < input.Length; i += 64)
                {
                    int index = 1;
                    while (index < 64 && input.Position < input.Length)
                    {
                        int code = Tree2.DecodeToken (reader);
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
                        int v = reader.GetBits (code);
                        if (code != 0 && 0 == (v >> (code - 1)))
                            v = (-1 << code | v) + 1;
                        color_data[i + block_fill_order[index]] = (short)v;
                        ++index;
                    }
                }
                if (8 == BPP)
                    DecodeGrayscale (color_data, dst);
                else
                    DecodeRGB (color_data, dst);
            }
        }

        static readonly byte[] block_fill_order =
        {
            0,  1,  8,  16, 9,  2,  3,  10, 17, 24, 32, 25, 18, 11, 4,  5,
            12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6,  7,  14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        };

        ThreadLocal<short[,]> s_YCbCr_block = new ThreadLocal<short[,]> (() => new short[64, 3]);

        short[,] YCbCr_block { get { return s_YCbCr_block.Value; } }

        void DecodeRGB (short[] data, int dst)
        {
            int block_count = Width / 8;
            for (int i = 0; i < block_count; ++i)
            {
                int src = i * 64;
                for (int channel = 0; channel < 3; ++channel)
                {
                    DecodeDCT (channel, data, src);
                    src += Width * 8;
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
                    int p = (y * Width + x) * 4;
                    Output[dst+p]   = FloatToByte (b);
                    Output[dst+p+1] = FloatToByte (g);
                    Output[dst+p+2] = FloatToByte (r);
                }
                dst += 32;
            }
        }

        void DecodeGrayscale (short[] data, int dst)
        {
            int src = 0;
            int block_count = Width / 8;
            for (int i = 0; i < block_count; ++i)
            {
                DecodeDCT (0, data, src);
                src += 64;
                for (int j = 0; j < 64; ++j)
                {
                    int y = j >> 3;
                    int x = j & 7;
                    int p = (y * Width + x) * 4;
                    Output[dst+p]   = (byte)YCbCr_block[j,0];
                    Output[dst+p+1] = (byte)YCbCr_block[j,0];
                    Output[dst+p+2] = (byte)YCbCr_block[j,0];
                }
                dst += 32;
            }
        }

        public void UnpackAlpha (int offset)
        {
            using (var input = new BinMemoryStream (this.Input, offset, Input.Length-offset))
            {
                if (1 != input.ReadInt32())
                    return;
                int dst = 3;
                int ctl = 1 << 1;
                while (dst < Output.Length)
                {
                    ctl >>= 1;
                    if (1 == ctl)
                        ctl = input.ReadUInt8() | 0x100;

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

                        int src = dst + (x + y * Width) * 4;
                        if (src < 0 || src >= dst)
                            return;

                        for (int i = 0; i < count; ++i)
                        {
                            Output[dst] = Output[src];
                            src += 4;
                            dst += 4;
                        }
                    }
                    else
                    {
                        Output[dst] = input.ReadUInt8();
                        dst += 4;
                    }
                }
                HasAlpha = true;
            }
        }

        ThreadLocal<float[,]> s_tmp = new ThreadLocal<float[,]> (() => new float[8,8]);

        void DecodeDCT (int channel, short[] data, int src)
        {
            float v1, v2, v3, v4, v5, v6, v7, v8;
            float v9, v10, v11, v12, v13, v14, v15, v16, v17;
            int d = channel > 0 ? 1 : 0;
            var tmp = s_tmp.Value;

            for (int i = 0; i < 8; ++i)
            {
                if (0 == data[src + 8 + i] && 0 == data[src + 16 + i] && 0 == data[src + 24 + i]
                    && 0 == data[src + 32 + i] && 0 == data[src + 40 + i] && 0 == data[src + 48 + i]
                    && 0 == data[src + 56 + i])
                {
                    var t = data[src + i] * DCT[d, i];
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

                v1 = data[src + i] * DCT[d,i];
                v2 = data[src + 8 + i]  * DCT[d, 8 + i];
                v3 = data[src + 16 + i] * DCT[d, 16 + i];
                v4 = data[src + 24 + i] * DCT[d, 24 + i];
                v5 = data[src + 32 + i] * DCT[d, 32 + i];
                v6 = data[src + 40 + i] * DCT[d, 40 + i];
                v7 = data[src + 48 + i] * DCT[d, 48 + i];
                v8 = data[src + 56 + i] * DCT[d, 56 + i];

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

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                s_YCbCr_block.Dispose();
                s_tmp.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
