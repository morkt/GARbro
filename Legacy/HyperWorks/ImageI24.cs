//! \file       ImageI24.cs
//! \date       2019 Jun 22
//! \brief      HyperWorks image format.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [980626][Love Gun] ACE OF SPADES 2

namespace GameRes.Formats.HyperWorks
{
    [Export(typeof(ImageFormat))]
    public class I24Format : ImageFormat
    {
        public override string         Tag { get { return "I24"; } }
        public override string Description { get { return "HyperWorks image format"; } }
        public override uint     Signature { get { return 0x41343249; } } // 'I24A'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToInt16 (0x10);
            if (bpp != 24)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP    = bpp,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new I24Decoder (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("I24Format.Write not implemented");
        }
    }

    internal class I24Decoder
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;

        public I24Decoder (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        static readonly short[] shiftTable = new short[] {
            -1, 0, 0, 1, 1, 1, -1, 1, 2, 1, -2, 1, -2, 0, 0, 2, 1, 2, -1, 2, -3, 0
        };

        bool cacheEmpty = true;
        int byteCount = 0;
        int bits = 0;
        int bitCount = 0;

        class Node
        {
            public Node     next;
            public int      depth;
            public int      token;
        }

        struct DictRec
        {
            public Link     link;
            public int      bitSize;
            public int      token;
        }

        class Link
        {
            public Link[]   children = new Link[2];
            public int      token;
        }

        DictRec[] dTable_1 = new DictRec[256];
        DictRec[] dTable_2 = new DictRec[256];
        DictRec[] dTable_3 = new DictRec[256];

        Link[] tBuffer_1 = InitNodeList<Link> (684);
        Link[] tBuffer_2 = InitNodeList<Link> (22);
        Link[] tBuffer_3 = InitNodeList<Link> (502);

        Node[] bTable_1 = InitNodeList<Node> (342);
        Node[] bTable_2 = InitNodeList<Node> (11);
        Node[] bTable_3 = InitNodeList<Node> (251);

        static T[] InitNodeList<T>(int count) where T : new()
        {
            var list = new T[count];
            for (int i = 0; i < count; ++i)
                list[i] = new T();
            return list;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x18;
            int stride = m_info.iWidth * 4;
            var pixels = new byte[stride * m_info.iHeight];
            byte[][] line_buffer = new[] { new byte[stride], new byte[stride], new byte[stride] };
            int dst = 0;
            var shift_table = shiftTable.Clone() as short[];
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                var line = line_buffer[2];
                line_buffer[2] = line_buffer[1];
                line_buffer[1] = line_buffer[0];
                line_buffer[0] = line;
                int x = 0;
                int p = 0;
                while (x < m_info.iWidth)
                {
                    if (byteCount-- == 0)
                    {
                        if (cacheEmpty)
                        {
                            cacheEmpty = false;
                            int val = ReadUInt8() << 8;
                            val |= ReadUInt8();
                            bits = val;
                            bitCount = 8;
                        }
                        InitTree (bTable_1, 342);
                        InitTree (bTable_2, 11);
                        InitTree (bTable_3, 251);

                        RebuildTree(bTable_1, dTable_1, tBuffer_1, 342);
                        RebuildTree(bTable_2, dTable_2, tBuffer_2, 11);
                        RebuildTree(bTable_3, dTable_3, tBuffer_3, 251);
                        byteCount = 0x3FFF;
                    }
                    int color_token = GetToken (dTable_1);
                    int shift_token = GetToken (dTable_2);

                    int shift_idx = 2 * shift_token;
                    short s1 = shift_table[shift_idx];
                    short s2 = shift_table[shift_idx + 1];
                    if (shift_token != 0)
                    {
                        while (shift_token --> 0)
                        {
                            shift_table[shift_idx]   = shift_table[shift_idx - 2];
                            shift_table[shift_idx+1] = shift_table[shift_idx - 1];
                            shift_idx -= 2;
                        }
                        shift_table[0] = s1;
                        shift_table[1] = s2;
                    }
                    int src = 4 * (x + s1);
                    if (color_token >= 216)
                    {
                        int count = color_token - 214;
                        x += count;
                        while (count --> 0)
                        {
                            line[p++] = line_buffer[s2][src++];
                            line[p++] = line_buffer[s2][src++];
                            line[p++] = line_buffer[s2][src++];
                            line[p++] = line_buffer[s2][src++];
                        }
                    }
                    else
                    {
                        sbyte r = shift_R[color_token];
                        if (r == -3)
                            r = (sbyte)(GetToken (dTable_3) + 3);
                        line[p+2] = (byte)(line_buffer[s2][src+2] - r);

                        sbyte g = shift_G[color_token];
                        if (g == -3)
                            g = (sbyte)(GetToken(dTable_3) + 3);
                        line[p+1] = (byte)(line_buffer[s2][src+1] - g);

                        sbyte b = shift_B[color_token];
                        if (b == -3)
                            b = (sbyte)(GetToken(dTable_3) + 3);
                        line[p] = (byte)(line_buffer[s2][src] - b);
                        line[p+3] = 0;
                        p += 4;
                        ++x;
                    }
                }
                Buffer.BlockCopy (line_buffer[0], 0, pixels, dst, stride);
                dst += stride;
            }
            return ImageData.Create (m_info, PixelFormats.Bgr32, null, pixels, stride);
        }

        private void InitTree (Node[] tree, int count) // sub_408FB0
        {
            for (int i = 0; i < count; ++i)
            {
                tree[i].next = null;
                tree[i].depth = 0;
                tree[i].token = i;
            }
            int length = GetBitLength();
            if (length <= 1)
                return;
            length -= 1;
            int fieldWidth = GetBits (3);
            int tIdx = 0;
            while (length > 0)
            {
                if (GetNextBit() != 0)
                {
                    tree[tIdx++].depth = GetBits (fieldWidth);
                    --length;
                }
                else
                {
                    int step = GetBitLength();
                    if (0 == step)
                        tIdx++;
                    else
                        tIdx += step;
                }
            }
        }

        private void RebuildTree (Node[] tree, DictRec[] dict, Link[] links, int count)
        {
            for (int i = 0; i < 256; ++i)
            {
                dict[i].token = 0;
                dict[i].link = null;
            }
            int next_idx = 0; // var node = tree;
            count -= 1;
            while (0 == tree[next_idx].depth)
            {
                if (--count <= 0)
                    break;
                ++next_idx;
            }
            if (0 == count)
                return;

            var node = tree[next_idx++];
            while (count --> 0)
            {
                int depth = tree[next_idx].depth;
                if (depth != 0)
                {
                    if (node != null)
                    {
                        if (node.depth <= depth)
                        {
                            var prev = node;
                            var ptr = node.next;
                            while (ptr != null)
                            {
                                if (ptr.depth > depth)
                                    break;
                                prev = ptr;
                                ptr = ptr.next;
                            }
                            prev.next = tree[next_idx];
                            tree[next_idx].next = ptr;
                        }
                        else
                        {
                            tree[next_idx].next = node;
                            node = tree[next_idx];
                        }
                    }
                }
                ++next_idx;
            }

            int bit_size = 0;
            int t3_i = 0;
            int dict_idx = 0;
            while (node != null)
            {
                if (node.depth > bit_size)
                {
                    dict_idx <<= node.depth - bit_size;
                    bit_size = node.depth;
                }
                if (bit_size >= 8)
                {
                    if (bit_size == 8)
                    {
                        dict[dict_idx].bitSize = 8;
                        dict[dict_idx].token = node.token;
                        dict[dict_idx].link = null;
                    }
                    else
                    {
                        int d1 = bit_size - 8;
                        int d2 = dict_idx >> d1;
                        int d3 = dict_idx << (32 - (bit_size - 8));
                        dict[d2].bitSize = 0;
                        var ptr_t2 = d2; // &dict[d2];
                        var link = dict[ptr_t2].link;
                        if (null == link)
                        {
                            var t3_ptr = links[t3_i];
                            t3_ptr.children[0] = null;
                            t3_ptr.children[1] = null;
                            t3_ptr.token = 0;
                            link = t3_ptr;
                            dict[ptr_t2].link = t3_ptr;
                            t3_i++;
                        }
                        while (d1 --> 0)
                        {
                            int v26 = (d3 >> 31) & 1;
                            d3 <<= 1;
                            if (null == link.children[v26])
                            {
                                var t3_ptr = links[t3_i];
                                t3_ptr.children[0] = null;
                                t3_ptr.children[1] = null;
                                t3_ptr.token = 0;
                                link.children[v26] = t3_ptr;
                                t3_i++;
                            }
                            link = link.children[v26];
                        }
                        link.token = node.token;
                    }
                }
                else
                {
                    int d1 = dict_idx << (8 - bit_size);
                    int d2 = 1 << (8 - bit_size);
                    while (d2 --> 0)
                    {
                        dict[d1].bitSize = bit_size;
                        dict[d1].token = node.token;
                        d1++;
                    }
                }
                ++dict_idx;
                node = node.next;
            }
        }

        int GetToken (DictRec[] table)
        {
            var table_ptr = table[(bits >> 8) & 0xFF];
            int count = table_ptr.bitSize;
            if (count != 0)
            {
                if (count >= bitCount)
                {
                    bits <<= bitCount;
                    count -= bitCount;
                    bits |= ReadUInt8();
                    bitCount = 8;
                }
                bits <<= count;
                bitCount -= count;
                return table_ptr.token;
            }
            else
            {
                bits = ReadUInt8() | bits << bitCount;
                bits <<= 8 - bitCount;
                Link link = table_ptr.link;
                do
                {
                    int path = GetNextBit() & 1;
                    link = link.children[path];
                    if (null == link)
                        throw new InvalidFormatException ("Invalid tree path");
                }
                while (link.children[0] != null);
                return link.token;
            }
        }

        private byte ReadUInt8()
        {
            return (byte)m_input.ReadByte();
        }

        private int GetNextBit ()
        {
            bits <<= 1;
            if (0 == --bitCount)
            {
                bits |= ReadUInt8();
                bitCount = 8;
            }
            return (bits >> 16) & 1;
        }

        private int GetBitLength ()
        {
            if (GetNextBit() != 0)
                return 0;
            int i = 0;
            do
            {
                ++i;
            }
            while (0 == GetNextBit());
            return (1 << i) | GetBits (i);
        }

        private int GetBits (int count)
        {
            int n = count;
            if (count >= bitCount)
            {
                bits <<= bitCount;
                n = count - bitCount;
                bits |= ReadUInt8();
                bitCount = 8;
            }
            if (n >= 8)
            {
                bits <<= 8;
                bits |= ReadUInt8();
                n -= 8;
            }
            bits <<= n;
            bitCount -= n;
            return (bits >> 16) & bitMask[count];
        }

        static readonly int[] bitMask = new[] {
            0x00000000, 0x00000001, 0x00000003, 0x00000007, 0x0000000F, 0x0000001F, 0x0000003F, 0x0000007F,
            0x000000FF, 0x000001FF, 0x000003FF, 0x000007FF, 0x00000FFF, 0x00001FFF, 0x00003FFF, 0x00007FFF,
            0x0000FFFF, 0x0001FFFF, 0x0003FFFF, 0x0007FFFF, 0x000FFFFF, 0x001FFFFF, 0x003FFFFF, 0x007FFFFF,
            0x00FFFFFF, 0x01FFFFFF, 0x03FFFFFF, 0x07FFFFFF, 0x0FFFFFFF, 0x1FFFFFFF, 0x3FFFFFFF, 0x7FFFFFFF
        };

        static readonly sbyte[] shift_R = new sbyte[] {
            -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3,
            -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -3, -2, -2, -2, -2,
            -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2,
            -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -2, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        };
        static readonly sbyte[] shift_G = new sbyte[] {
            -3, -3, -3, -3, -3, -3, -2, -2, -2, -2, -2, -2, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0,
            0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, -3, -3, -3, -3, -3, -3, -2, -2, -2,
            -2, -2, -2, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2,
            2, 2, 2, 2, -3, -3, -3, -3, -3, -3, -2, -2, -2, -2, -2, -2, -1, -1, -1, -1,
            -1, -1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, -3, -3, -3, -3,
            -3, -3, -2, -2, -2, -2, -2, -2, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 1,
            1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, -3, -3, -3, -3, -3, -3, -2, -2, -2, -2, -2,
            -2, -1, -1, -1, -1, -1, -1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2,
            2, 2, -3, -3, -3, -3, -3, -3, -2, -2, -2, -2, -2, -2, -1, -1, -1, -1, -1, -1,
            0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2,
        };
        static readonly sbyte[] shift_B = new sbyte[] {
            -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2,
            -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0,
            1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2,
            -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2,
            -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0,
            1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2,
            -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2,
            -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0,
            1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2,
            -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, -3, -2, -1, 0, 1, 2, 
        };
    }
}
