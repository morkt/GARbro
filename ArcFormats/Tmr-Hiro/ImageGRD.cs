//! \file       ImageGRD.cs
//! \date       Wed Dec 23 17:00:30 2015
//! \brief      Tmr-Hiro ADV System image format.
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Collections.Generic;

namespace GameRes.Formats.TmrHiro
{
    internal class GrdMetaData : ImageMetaData
    {
        public int      Format;
        public int      AlphaSize;
        public int      RSize;
        public int      GSize;
        public int      BSize;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro ADV System image format"; } }
        public override uint     Signature { get { return 0; } }

        public GrdFormat ()
        {
            Extensions = new string[] { "grd", "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            if (header[0] != 1 && header[0] != 2)
                return null;
            if (header[1] != 1 && header[1] != 0xA1 && header[1] != 0xA2)
                return null;
            int bpp = header.ToUInt16 (6);
            if (bpp != 24 && bpp != 32)
                return null;
            int screen_width  = header.ToUInt16 (2);
            int screen_height = header.ToUInt16 (4);
            int left    = header.ToUInt16 (8);
            int right   = header.ToUInt16 (0xA);
            int top     = header.ToUInt16 (0xC);
            int bottom  = header.ToUInt16 (0xE);
            var info = new GrdMetaData {
                Format      = header.ToUInt16 (0),
                Width       = (uint)System.Math.Abs (right - left),
                Height      = (uint)System.Math.Abs (bottom - top),
                BPP         = bpp,
                OffsetX     = left,
                OffsetY     = screen_height - bottom,
                AlphaSize   = header.ToInt32 (0x10),
                RSize       = header.ToInt32 (0x14),
                GSize       = header.ToInt32 (0x18),
                BSize       = header.ToInt32 (0x1C),
            };
            if (0x20 + info.AlphaSize + info.RSize + info.BSize + info.GSize != stream.Length)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GrdMetaData)info;
            var reader = new GrdReader (stream.AsStream, meta);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrdFormat.Write not implemented");
        }
    }

    internal sealed class GrdReader
    {
        Stream      m_input;
        GrdMetaData m_info;
        byte[]      m_output;
        int         m_pack_type;
        int         m_pixel_size;
        byte[]      m_channel;

        public PixelFormat Format { get; private set; }
        public        byte[] Data { get { return m_output; } }

        public GrdReader (Stream input, GrdMetaData info)
        {
            m_input = input;
            m_info  = info;
            if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (m_info.AlphaSize > 0)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr32;
            int channel_size = (int)(m_info.Width * m_info.Height);
            m_pack_type = m_info.Format >> 8;
            m_pixel_size = m_info.BPP / 8;
            m_output = new byte[m_pixel_size * channel_size];
            m_channel = new byte[channel_size];
        }

        public void Unpack ()
        {
            int next_pos = 0x20;
            if (32 == m_info.BPP && m_info.AlphaSize > 0)
            {
                UnpackChannel (3, next_pos, m_info.AlphaSize);
                next_pos += m_info.AlphaSize;
            }
            UnpackChannel (2, next_pos, m_info.RSize);
            next_pos += m_info.RSize;
            UnpackChannel (1, next_pos, m_info.GSize);
            next_pos += m_info.GSize;
            UnpackChannel (0, next_pos, m_info.BSize);
        }

        void UnpackChannel (int dst, int src_pos, int src_size)
        {
            m_input.Position = src_pos;

            if (1 == m_pack_type)
            {
                UnpackRLE (m_input, src_size);
            }
            else
            {
                var data = UnpackHuffman (m_input);
                if (0xA2 == m_pack_type)
                {
                    UnpackLZ77 (data, m_channel);
                }
                else
                {
                    using (var mem = new MemoryStream (data))
                        UnpackRLE (mem, data.Length);
                }
            }
            for (int y = (int)m_info.Height-1; y >= 0; --y)
            {
                int src = y * (int)m_info.Width;
                for (uint x = 0; x < m_info.Width; ++x)
                {
                    m_output[dst] = m_channel[src++];
                    dst += m_pixel_size;
                }
            }
        }

        void UnpackRLE (Stream input, int src_size)
        {
            int src = 0;
            int dst = 0;
            while (src < src_size)
            {
                int count = input.ReadByte();
                if (-1 == count)
                    return;
                ++src;
                if (count > 0x7F)
                {
                    count &= 0x7F;
                    byte v = (byte)input.ReadByte();
                    ++src;
                    for (int i = 0; i < count; ++i)
                        m_channel[dst++] = v;
                }
                else if (count > 0)
                {
                    input.Read (m_channel, dst, count);
                    src += count;
                    dst += count;
                }
            }
        }

        static void UnpackLZ77 (byte[] input, byte[] output)
        {
            var special = input[8];
            int src = 12;
            int dst = 0;
            while (dst < output.Length)
            {
                byte b = input[src++];
                if (b == special)
                {
                    byte offset = input[src++];
                    if (offset != special)
                    {
                        byte count = input[src++];
                        if (offset > special)
                            --offset;

                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                        dst += count;
                    }
                    else
                        output[dst++] = offset;
                }
                else
                    output[dst++] = b;
            }
        }

        const int RootNodeIndex = 0x1FE;
        int m_huffman_unpacked;

        byte[] UnpackHuffman (Stream input)
        {
            var tree = CreateHuffmanTree (input);
            var unpacked = new byte[m_huffman_unpacked];
            using (var bits = new LsbBitStream (input, true))
            {
                int dst = 0;
                while (dst < m_huffman_unpacked)
                {
                    int node = RootNodeIndex;
                    while (node > 0xFF)
                    {
                        if (0 != bits.GetNextBit())
                            node = tree[node].Right;
                        else
                            node = tree[node].Left;
                    }
                    unpacked[dst++] = (byte)node;
                }
            }
            return unpacked;
        }

        HuffmanNode[] CreateHuffmanTree (Stream input)
        {
            var nodes = new HuffmanNode[0x200];
            var tree = new List<int> (0x100);
            using (var reader = new ArcView.Reader (input))
            {
                m_huffman_unpacked = reader.ReadInt32();
                reader.ReadInt32(); // packed_size

                for (int i = 0; i < 0x100; i++)
                {
                    nodes[i].Freq = reader.ReadUInt32();
                    AddNode (tree, nodes, i);
                }
            }
            int last_node = 0x100;
            while (tree.Count > 1)
            {
                int l = tree[0];
                tree.RemoveAt (0);
                int r = tree[0];
                tree.RemoveAt (0);
                nodes[last_node].Freq = nodes[l].Freq + nodes[r].Freq;
                nodes[last_node].Left = l;
                nodes[last_node].Right = r;
                AddNode (tree, nodes, last_node++);
            }
            return nodes;
        }

        static void AddNode (List<int> tree, HuffmanNode[] nodes, int index)
        {
            uint freq = nodes[index].Freq;
            int i;
            for (i = 0; i < tree.Count; ++i)
                if (nodes[tree[i]].Freq > freq)
                    break;
            tree.Insert (i, index);
        }

        internal struct HuffmanNode
        {
            public uint Freq;
            public int  Left;
            public int  Right;
        }
    }
}
