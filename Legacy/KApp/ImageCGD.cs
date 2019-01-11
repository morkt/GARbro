//! \file       ImageCGD.cs
//! \date       2019 Jan 10
//! \brief      Spiel compressed image.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

// [030228][spiel] The Black Box

namespace GameRes.Formats.KApp
{
    [Export(typeof(ImageFormat))]
    public class CgdFormat : ImageFormat
    {
        public override string         Tag { get { return "CGD/KTOOL"; } }
        public override string Description { get { return "KApp compressed image format"; } }
        public override uint     Signature { get { return 0x6F6F746B; } } // 'ktool210'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual ("ktool210") || header.ToInt32 (8) != 1)
                return null;
            uint offset = header.ToUInt32 (0x10) & 0x7FFFFFFF;
            return CgdMetaData.FromStream (file, offset);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new CgdDecoder (file, (CgdMetaData)info);
            return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgdFormat.Write not implemented");
        }
    }

    internal class CgdMetaData : ImageMetaData
    {
        public uint DataOffset;
        public int  UnpackedSize;
        public byte Compression;

        internal static CgdMetaData FromStream (IBinaryStream file, uint offset)
        {
            file.Position = offset;
            int unpacked_size = file.ReadInt32();
            file.ReadInt32();
            byte compression = (byte)file.ReadUInt16();
            uint header_size = file.ReadUInt16();
            uint id = file.ReadUInt32();
            if (header_size < 0x10 || (id != 0x973768 && id != 0xB29EA4))
                return null;
            ushort width  = file.ReadUInt16();
            ushort height = file.ReadUInt16();
            ushort bpp    = file.ReadUInt16();
            return new CgdMetaData {
                Width  = width,
                Height = height,
                BPP    = bpp,
                DataOffset = offset + 0x10 + header_size,
                UnpackedSize = unpacked_size,
                Compression = compression,
            };
        }
    }

    internal class KTool
    {
        public static void Unpack (IBinaryStream input, byte[] output, byte method)
        {
            switch (method)
            {
            case 0: input.Read (output, 0, output.Length); break; 
            case 1: DecompressRle (input, output, 1); break;
            case 2: DecompressRle (input, output, 2); break;
            case 3: DecompressRle (input, output, 3); break;
            case 4: DecompressRle (input, output, 4); break;
            case 0x10: DecompressHuffman (input, output); break;
            default:
                throw new InvalidFormatException();
            }
        }

        internal static void DecompressRle (IBinaryStream input, byte[] output, int step)
        {
            for (int i = 0; i < step; ++i)
            {
                sbyte ctl = input.ReadInt8();
                int dst = i;
                while (ctl != 0)
                {
                    if (ctl < 0)
                    {
                        int count = -ctl;
                        while (count --> 0)
                        {
                            output[dst] = input.ReadUInt8();
                            dst += step;
                        }
                    }
                    else
                    {
                        byte v = input.ReadUInt8();
                        int count = ctl;
                        while (count --> 0)
                        {
                            output[dst] = v;
                            dst += step;
                        }
                    }
                    ctl = input.ReadInt8();
                }
            }
        }

        internal static void DecompressHuffman (IBinaryStream input, byte[] output)
        {
            var decomp = new HuffmanDecoder (input);
            decomp.Unpack (output);
        }

        struct HuffmanNode
        {
            public ushort   Code;
            public ushort   LNode;
            public ushort   RNode;
        }

        class HuffmanDecoder
        {
            IBinaryStream   m_input;
            HuffmanNode[]   m_tree = new HuffmanNode[514];

            public HuffmanDecoder (IBinaryStream input)
            {
                m_input = input;
            }

            public void Unpack (byte[] output)
            {
                ReadDict();
                var root = BuildTree();
                int dst = 0;
                int bits = 0;
                byte mask = 0;
                while (dst < output.Length)
                {
                    var token = root;
                    while (token > 0x100)
                    {
                        if (0 == mask)
                        {
                            bits = m_input.ReadByte();
                            if (-1 == bits)
                                return;
                            mask = 0x80;
                        }
                        if ((bits & mask) != 0)
                            token = m_tree[token].RNode;
                        else
                            token = m_tree[token].LNode;
                        mask >>= 1;
                    }
                    output[dst++] = (byte)token;
                }
            }

            void ReadDict ()
            {
                var dict = new byte[256];
                DecompressRle (m_input, dict, 1);
                for (int i = 0; i < 256; ++i)
                {
                    m_tree[i].Code = dict[i];
                }
                m_tree[256].Code = 1;
            }

            ushort BuildTree ()
            {
                m_tree[513].Code = ushort.MaxValue;
                ushort root = 257;
                while (root > 0)
                {
                    ushort rhs = 513;
                    ushort lhs = 513;
                    ushort node = 0;
                    for (ushort i = 0; i < root; ++i)
                    {
                        var code = m_tree[node].Code;
                        if (code != 0)
                        {
                            if (code < m_tree[lhs].Code)
                            {
                                rhs = lhs;
                                lhs = i;
                            }
                            else if (code < m_tree[rhs].Code)
                            {
                                rhs = i;
                            }
                        }
                        ++node;
                    }
                    if (rhs == 513)
                        break;
                    m_tree[root].Code = (ushort)(m_tree[rhs].Code + m_tree[lhs].Code);
                    m_tree[root].LNode = lhs;
                    m_tree[root].RNode = rhs;
                    m_tree[lhs].Code = 0;
                    m_tree[rhs].Code = 0;
                    ++root;
                }
                return (ushort)(root - 1);
            }
        }
    }

    internal class CgdDecoder : BinaryImageDecoder
    {
        public CgdDecoder (IBinaryStream input, CgdMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            var meta = (CgdMetaData)Info;
            m_input.Position = meta.DataOffset;
            var pixels = new byte[meta.UnpackedSize];
            KTool.Unpack (m_input, pixels, meta.Compression);
            PixelFormat format = 24 == meta.BPP ? PixelFormats.Rgb24 : PixelFormats.Bgra32;
            return ImageData.Create (meta, format, null, pixels);
        }
    }
}
