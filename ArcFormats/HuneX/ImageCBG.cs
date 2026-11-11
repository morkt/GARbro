//! \file       ImageCBG.cs
//! \date       2026-02-19
//! \brief      HUNEX General Game Engine image format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.HuneX
{
    internal class CbgMetaData : ImageMetaData
    {
        public uint StripeHeight;
    }

    [Export(typeof(ImageFormat))]
    public class CompressedBGFormat : ImageFormat
    {
        public override string         Tag { get { return "CompressedBG_MT"; } }
        public override string Description { get { return "HUNEX General Game Engine compressed image format"; } }
        public override uint     Signature { get { return 0x706D6F43; } }

        public CompressedBGFormat ()
        {
            Extensions = new string[] { "cbg" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            if (!header.AsciiEqual ("CompressedBG_MT"))
                return null;
            return new CbgMetaData
            {
                Width = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                StripeHeight = header.ToUInt32 (0x18),
                BPP = header.ToInt32 (0x1C),
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

    internal class CbgReader : LsbBitStream
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
            switch (m_info.BPP)
            {
            case 32: Format = PixelFormats.Bgra32; break;
            case 24: Format = PixelFormats.Bgr24; break;
            case 8:  Format = PixelFormats.Gray8; break;
            default: throw new InvalidFormatException();
            }
        }

        public void Unpack ()
        {
            uint count = (m_info.Height + m_info.StripeHeight - 1) / m_info.StripeHeight;
            var len = new byte[4];
            var offsets = new uint[count];
            m_output = new byte[Stride * m_info.Height];
            m_input.Position = 0x30;
            for (int i = 0; i < count; i++)
            {
                m_input.Read (len, 0, 4);
                offsets[i] = BitConverter.ToUInt32 (len, 0);
            }
            for (int i = 0; i < count; i++)
            {
                m_input.Seek (offsets[i], SeekOrigin.Begin);
                uint height = (uint)Math.Min (m_info.StripeHeight, m_info.Height - m_info.StripeHeight * i);
                m_input.Read (len, 0, 4);
                var packed = new byte[BitConverter.ToUInt32 (len, 0)];
                var weights = ReadWeightTable (m_input, 0x100);
                var tree = new HuffmanTree (weights);
                tree.Build(0x1ff);
                HuffmanDecompress (tree, packed);
                var stripe_output = new byte[Stride * height];
                UnpackZeros (packed, stripe_output);
                ReverseAverageSampling (stripe_output, height);
                Buffer.BlockCopy (stripe_output, 0, m_output, (int)(Stride * m_info.StripeHeight * i), stripe_output.Length);
            }
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

        static protected int[] ReadWeightTable (Stream input, int length)
        {
            int[] leaf_nodes_weight = new int[length];
            for (int i = 0; i < length; ++i)
            {
                int weight = ReadInteger (input);
                if (-1 == weight)
                    throw new InvalidFormatException ("Invalid compressed stream");
                leaf_nodes_weight[i] = weight;
            }
            return leaf_nodes_weight;
        }

        void HuffmanDecompress (HuffmanTree tree, byte[] output)
        {
            this.Reset();
            for (int dst = 0; dst < output.Length; dst++)
            {
                output[dst] = (byte)tree.DecodeSequence (this);
            }
        }

        void UnpackZeros (byte[] input, byte[] output)
        {
            int dst = 0;
            int dec_zero = 0;
            int src = 0;
            while (dst < output.Length)
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

                if (dst + count > output.Length)
                    break;

                if (0 == dec_zero)
                {
                    if (src + count > input.Length)
                        break;
                    Buffer.BlockCopy (input, src, output, dst, count);
                    src += count;
                }
                else
                {
                    for (int i = 0; i < count; ++i)
                        output[dst+i] = 0;
                }
                dec_zero ^= 1;
                dst += count;
            }
        }

        void ReverseAverageSampling (byte[] output, uint height)
        {
            for (int y = 0; y < height; ++y)
            {
                int line = y * Stride;
                for (int x = 0; x < m_info.Width; ++x)
                {
                    int pixel = line + x * m_pixel_size;
                    for (int p = 0; p < m_pixel_size; p++)
                    {
                        int avg = 0;
                        if (x > 0)
                            avg += output[pixel + p - m_pixel_size];
                        if (y > 0)
                            avg += output[pixel + p - Stride];
                        if (x > 0 && y > 0)
                            avg /= 2;
                        if (0 != avg)
                            output[pixel + p] += (byte)avg;
                    }
                }
            }
        } 
    }
}
