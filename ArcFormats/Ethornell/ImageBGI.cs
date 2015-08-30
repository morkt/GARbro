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
                return ImageData.Create (meta, reader.Format, null, reader.Data);
            }
        }
    }

    internal class CbgReader : BgiDecoderBase
    {
        byte[]          m_output;
        CbgMetaData     m_info;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public CbgReader (Stream input, CbgMetaData info) : base (input, true)
        {
            m_info = info;
            int stride = (int)info.Width * info.BPP / 8;
            m_output = new byte[stride * (int)info.Height];
            m_key = m_info.Key;
            m_magic = 0;
            switch (m_info.BPP)
            {
            case 32: Format = PixelFormats.Bgra32; break;
            case 24: Format = PixelFormats.Bgr24; break;
            case 16: Format = PixelFormats.Bgr565; break;
            case 8:  Format = PixelFormats.Gray8; break;
            default: throw new InvalidFormatException();
            }
        }

        public void Unpack ()
        {
            Input.Position = 0x30;
            var enc_buf = new byte[m_info.EncLength];
            if (enc_buf.Length != Input.Read (enc_buf, 0, enc_buf.Length))
                throw new EndOfStreamException();
            byte sum = 0;
            byte xor = 0;
            int enc_pos = 0;
            uint[] leaf_nodes_weight = new uint[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                uint weight = 0;
                byte code;
                int code_length = 0;
                do
                {
                    if (enc_pos >= enc_buf.Length)
                        throw new InvalidFormatException ("Invalid compressed stream");
                    code = enc_buf[enc_pos++];
                    code -= UpdateKey();
                    sum += code;
                    xor ^= code;
                    weight |= ((uint)code & 0x7f) << code_length;
                    code_length += 7;
                }
                while (0 != (code & 0x80));
                leaf_nodes_weight[i] = weight;
            }
            if (sum != m_info.CheckSum || xor != m_info.CheckXor)
                throw new InvalidFormatException ("Compressed stream failed checksum check");

            var nodes = new HuffmanNode[0x200];
            int root_index = CreateHuffmanTree (nodes, leaf_nodes_weight);
            byte[] packed = new byte[m_info.IntermediateLength];

            HuffmanDecompress (nodes, root_index, packed);
            UnpackZeros (packed);
            ReverseAverageSampling();
        }

        class HuffmanNode
        {
            public bool Valid;
            public bool IsParent;
            public uint Weight;
            public int  ParentIndex;
            public int  LeftChildIndex;
            public int  RightChildIndex;
        }

        int CreateHuffmanTree (HuffmanNode[] nodes, uint[] leaf_nodes_weight)
        {
            uint root_node_weight = 0;
            for (int i = 0; i < 0x100; ++i)
            {
                nodes[i] = new HuffmanNode
                {
                    Valid = leaf_nodes_weight[i] != 0,
                    Weight = leaf_nodes_weight[i],
                    IsParent = false
                };
                root_node_weight += nodes[i].Weight;
            }

            int parent_node_index = 0x100;
            int[] child_node_index = new int[2];
            for (;;)
            {
                var parent_node = new HuffmanNode();
                nodes[parent_node_index] = parent_node;
                for (int i = 0; i < 2; i++)
                {
                    uint min_weight = uint.MaxValue;
                    child_node_index[i] = -1;

                    for (int n = 0; n < parent_node_index; n++)
                    {
                        if (nodes[n].Valid)
                        {
                            if (nodes[n].Weight < min_weight)
                            {
                                min_weight = nodes[n].Weight;
                                child_node_index[i] = n;
                            }
                        }
                    }
                    nodes[child_node_index[i]].Valid = false;
                    nodes[child_node_index[i]].ParentIndex = parent_node_index;
                }
                parent_node.Valid = true;
                parent_node.IsParent = true;
                parent_node.LeftChildIndex  = child_node_index[0];
                parent_node.RightChildIndex = child_node_index[1];
                parent_node.Weight = nodes[parent_node.LeftChildIndex].Weight
                                   + nodes[parent_node.RightChildIndex].Weight;
                if (parent_node.Weight == root_node_weight)
                    break;
                ++parent_node_index;
            }
            return parent_node_index;
        }

        void HuffmanDecompress (HuffmanNode[] huffman_nodes, int root_node_index, byte[] output)
        {
            for (int dst = 0; dst < output.Length; dst++)
            {
                int node_index = root_node_index;
                do
                {
                    int bit = GetNextBit();
                    if (-1 == bit)
                        throw new EndOfStreamException();
                    if (0 == bit)
                        node_index = huffman_nodes[node_index].LeftChildIndex;
                    else
                        node_index = huffman_nodes[node_index].RightChildIndex;
                }
                while (huffman_nodes[node_index].IsParent);
                output[dst] = (byte)node_index;
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
            int pixel_size = m_info.BPP / 8;
            int stride = (int)m_info.Width * pixel_size;
            for (int y = 0; y < m_info.Height; ++y)
            {
                int line = y * stride;
                for (int x = 0; x < m_info.Width; ++x)
                {
                    int pixel = line + x * pixel_size;
                    for (int p = 0; p < pixel_size; p++)
                    {
                        int avg = 0;
                        if (x > 0)
                            avg += m_output[pixel + p - pixel_size];
                        if (y > 0)
                            avg += m_output[pixel + p - stride];
                        if (x > 0 && y > 0)
                            avg /= 2;
                        if (0 != avg)
                            m_output[pixel + p] += (byte)avg;
                    }
                }
            }
        }
    }
}
