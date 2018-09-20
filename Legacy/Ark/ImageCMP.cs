//! \file       ImageCMP.cs
//! \date       2018 Jul 26
//! \brief      Ark image format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [030328][Ark] Tsuki no Mori

namespace GameRes.Formats.Ark
{
    internal class CmpMetaData : ImageMetaData
    {
        public bool     HasAlpha;
        public int      AWidth;
        public int      AHeight;
        public uint[]   FreqTable;
        public int      DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class CmpFormat : ImageFormat
    {
        public override string         Tag { get { return "CMP/ARK"; } }
        public override string Description { get { return "Ark image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".cmp"))
                return null;
            var header = file.ReadHeader (5);
            uint width  = header.ToUInt16 (0);
            uint height = header.ToUInt16 (2);
            bool has_alpha = header[4] != 0;
            int aw = 0, ah = 0;
            if (has_alpha)
            {
                aw = file.ReadUInt16();
                ah = file.ReadUInt16();
            }
            var table = new uint[32];
            for (int i = 0; i < 32; ++i)
                table[i] = file.ReadUInt32();
            int packed_length = file.ReadInt32();
            long data_pos = file.Position;
            if (file.Length - data_pos != packed_length)
                return null;
            return new CmpMetaData {
                Width = width,
                Height = height,
                BPP = 32,
                HasAlpha = has_alpha,
                AWidth = aw,
                AHeight = ah,
                FreqTable = table,
                DataOffset = (int)data_pos,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CmpReader (file, (CmpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CmpFormat.Write not implemented");
        }
    }

    internal class CmpReader
    {
        IBinaryStream   m_input;
        CmpMetaData     m_info;
        int             m_stride;

        public PixelFormat Format { get; private set; }
        public int         Stride { get { return m_stride; } }

        public CmpReader (IBinaryStream input, CmpMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = (int)info.Width * 2;
            Format = PixelFormats.Bgr555;
        }

        public Array Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            int plane_size = (int)m_info.Width * (int)m_info.Height;
            var planes = new byte[plane_size * 3];
            using (var bits = new MsbBitStream (m_input.AsStream, true))
                UnpackHuffman (bits, planes);

            int rsrc = 0;
            int gsrc = plane_size;
            int bsrc = plane_size * 2;
            int width = m_stride / 2;
            var output = new ushort[width * (int)m_info.Height];
            int dst_row = output.Length - width;
            while (dst_row >= 0)
            {
                int dst = dst_row;
                for (int x = (int)m_info.Width; x > 0; --x)
                {
                    int color = planes[bsrc++] << 10 | planes[gsrc++] << 5 | planes[rsrc++];
                    output[dst++] = (ushort)color;
                }
                dst_row -= width;
            }
            return output;
        }

        void UnpackHuffman (MsbBitStream input, byte[] output)
        {
            var root = BuildHuffmanTree (m_info.FreqTable);
            int dst = 0;
            byte last_symbol = 0;
            while (dst < output.Length)
            {
                var node = root;
                while (node.Symbol > 0x1F)
                {
                    if (input.GetNextBit() != 0)
                        node = node.Left;
                    else
                        node = node.Right;
                }      
                byte symbol = (byte)(last_symbol + node.Symbol);
                if (symbol > 0x1F)
                    symbol -= 0x20;
                output[dst++] = symbol;
                last_symbol = symbol;
            }
        }

        class Node
        {
            public byte Symbol;
            public uint Freq;
            public Node Left;
            public Node Right;
        }

        Node BuildHuffmanTree (uint[] freq_table)
        {
            var tree = new List<Node> (256);
            for (byte i = 0; i < 32; ++i)
            {
                tree.Add (new Node { Symbol = i, Freq = freq_table[i] });
            }
            for (byte i = 32; i < 255; ++i)
            {
                tree.Add (new Node { Symbol = i });
            }
            while (tree.Count > 1)
            {
                Node[] child = { null, null };

                for (int i = 0; i < 2; i++)
                {
                    Node last_node = null;
                    uint min_freq = uint.MaxValue;
                    foreach (var node in tree)
                    {
                        if (node.Freq <= min_freq)
                        {
                            min_freq = node.Freq;
                            last_node = node;
                        }
                    }
                    child[i] = last_node;
                    tree.Remove (last_node);
                }
                tree.Add (new Node {
                    Symbol = 0xFF,
                    Freq = child[0].Freq + child[1].Freq,
                    Left = child[0],
                    Right = child[1]
                });
            }
            return tree[0];
        }
    }
}
