//! \file       ImageGCC.cs
//! \date       Mon Jun 29 05:12:05 2015
//! \brief      Ai5Win engine image format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    internal class GccMetaData : ImageMetaData
    {
        public uint Signature;
    }

    [Export(typeof(ImageFormat))]
    public class GccFormat : ImageFormat
    {
        public override string         Tag { get { return "GCC"; } }
        public override string Description { get { return "AI5WIN engine image format"; } }
        public override uint     Signature { get { return 0x6d343252; } } // 'R24m'

        public GccFormat ()
        {
            // 'R24m', 'R24n', 'G24m', 'G24n'
            Signatures = new uint[] { 0x6d343252, 0x6E343252, 0x6D343247, 0x6E343247 };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[12];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;

            return new GccMetaData
            {
                Width = LittleEndian.ToUInt16 (header, 8),
                Height = LittleEndian.ToUInt16 (header, 10),
                BPP = 'm' == header[3] ? 32 : 24,
                OffsetX = LittleEndian.ToInt16 (header, 4),
                OffsetY = LittleEndian.ToInt16 (header, 6),
                Signature = LittleEndian.ToUInt32 (header, 0),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as GccMetaData;
            if (null == meta)
                throw new ArgumentException ("GccFormat.Read should be supplied with GccMetaData", "info");

            var reader = new Reader (stream, meta);
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GccFormat.Write not implemented");
        }

        internal class Reader
        {
            byte[]          m_input;
            GccMetaData     m_info;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_alpha_w;
            int             m_alpha_h;

            public PixelFormat Format { get; private set; }
            public byte[]        Data { get { return m_output; } }

            public Reader (Stream input, GccMetaData info)
            {
                m_input = new byte[input.Length];
                input.Read (m_input, 0, m_input.Length);
                m_info = info;
                m_width = (int)m_info.Width;
                m_height = (int)m_info.Height;
            }

            public void Unpack ()
            {
                switch (m_info.Signature)
                {
                case 0x6E343247: UnpackNormal (LzssUnpack); break;  // G24n
                case 0x6D343247: UnpackMasked (LzssUnpack); break;  // G24m
                case 0x6E343252: UnpackNormal (AltUnpack); break;   // R24n
                case 0x6D343252: UnpackMasked (AltUnpack); break;   // R24m
                default: throw new NotSupportedException();
                }
            }

            private void UnpackNormal (Action<int> unpacker)
            {
                unpacker (0x14);
                FlipPixels (m_width*3);
                Format = PixelFormats.Bgr24;
            }

            private void UnpackMasked (Action<int> unpacker)
            {
                unpacker (0x20);
                var alpha = UnpackAlpha();
                if (m_alpha_w < (m_info.OffsetX + m_width) || m_alpha_h < (m_info.OffsetY + m_height))
                {
                    FlipPixels (m_width*3);
                    Format = PixelFormats.Bgr24;
                }
                else
                {
                    Convert24To32 (alpha);
                    Format = PixelFormats.Bgra32;
                }
            }

            private void FlipPixels (int stride)
            {
                // flip pixels vertically
                var pixels = new byte[m_output.Length];
                int dst = 0;
                for (int src = stride * (m_height-1); src >= 0; src -= stride)
                {
                    Buffer.BlockCopy (m_output, src, pixels, dst, stride);
                    dst += stride;
                }
                m_output = pixels;
            }

            private void Convert24To32 (byte[] alpha)
            {
                Debug.Assert (m_alpha_w >= (m_info.OffsetX + m_width) && m_alpha_h >= (m_info.OffsetY + m_height));
                int src_stride = m_width * 3; 
                var pixels = new byte[m_width * m_height * 4];
                int dst = 0;
                int alpha_row = m_alpha_w * (m_alpha_h - m_info.OffsetY - 1);
                for (int row = m_width * (m_height-1); row >= 0; row -= m_width)
                {
                    int src = row*3;
                    for (int x = 0; x < m_width; ++x)
                    {
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = m_output[src++];
                        pixels[dst++] = alpha[alpha_row + m_info.OffsetX + x];
                    }
                    alpha_row -= m_alpha_w;
                }
                m_output = pixels;
            }

            void LzssUnpack (int offset)
            {
                int out_length = m_width * m_height * 3;
                using (var input = new MemoryStream (m_input, offset, m_input.Length-offset))
                using (var lzss = new LzssReader (input, (int)input.Length, out_length))
                {
                    lzss.Unpack();
                    m_output = lzss.Data;
                }
            }

            int m_index;
            int m_current;
            int m_mask;

            void ResetBitInput (int idx)
            {
                m_index = idx;
                m_mask = 0x80;
            }

            bool NextBit ()
            {
                m_mask <<= 1;
                if (0x100 == m_mask)
                {
                    m_current = m_input[m_index++];
                    m_mask = 1;
                }
                return 0 != (m_current & m_mask);
            }

            byte[] UnpackAlpha () // sub_444FF0
            {
                m_alpha_w = LittleEndian.ToUInt16 (m_input, 0x18);
                m_alpha_h = LittleEndian.ToUInt16 (m_input, 0x1A);
                int total = m_alpha_w * m_alpha_h;
                var alpha = new byte[total];
                int offset = 0x20 + LittleEndian.ToInt32 (m_input, 0x0C);
                ResetBitInput (offset);
                int src = offset + LittleEndian.ToInt32 (m_input, 0x1C);
                int dst = 0;
                while (dst < total)
                {
                    if (NextBit())
                    {
                        int count = ReadCount();
                        byte v = m_input[src++];
                        for (int i = 0; i < count; ++ i)
                        {
                            alpha[dst++] = v;
                        }
                    }
                    else
                    {
                        alpha[dst++] = m_input[src++];
                    }
                }
                return alpha;
            }

            int ReadCount () // sub_444F60
            {
                int result = 1;
                int bit_count = 0;
                while (!NextBit())
                    ++bit_count;
                while (bit_count != 0)
                {
                    --bit_count;
                    result <<= 1;
                    if (NextBit())
                        result |= 1;
                }
                return result;
            }

            int m_dst;

            private void AltUnpack (int offset) // sub_445620
            {
                byte[] chunk = new byte[0x10001];

                int src = offset + LittleEndian.ToInt32 (m_input, 0x10); // within m_input
                ResetBitInput (offset);
                int total = 3 * m_width * m_height;
                m_output = new byte[total];
                m_dst = 0;
                int dst = 0;
                while (dst < total)
                {
                    int chunk_size = Math.Min (total - dst, 0xffff);
                    if (NextBit())
                    {
                        src = ReadCompressedChunk (src, chunk, chunk_size + 2);
                        DecodeChunk (chunk, chunk_size);
                    }
                    else
                    {
                        src = ReadRawChunk (src, chunk_size);
                    }
                    dst += chunk_size;
                }
                return;
            }

            ushort[] v15 = new ushort[0x100];
            ushort[] v16 = new ushort[0x100];
            ushort[] v17 = new ushort[0x10000];

            void DecodeChunk (byte[] chunk, int chunk_size) // sub_444E40
            {
                for (int i = 0; i < v15.Length; ++i)
                    v15[i] = 0;
                for (int i = 0; i < chunk_size; ++i)
                    ++v15[chunk[2+i]];
                ushort v7 = 0;
                for (int r = 0; r < 0x100; ++r)
                {
                    v16[r] = v7;
                    v7 += v15[r];
                    v15[r] = 0;
                }
                for (int v9 = 0; v9 < chunk_size; ++v9)
                {
                    int v10 = chunk[2+v9];
                    int r = v15[v10] + v16[v10];
                    v17[r] = (ushort)v9;
                    v15[v10]++;
                }
                int a3 = LittleEndian.ToUInt16 (chunk, 0);
                int v12 = v17[a3];
                for (int i = 0; i < chunk_size; ++i)
                {
                    m_output[m_dst++] = chunk[2+v12];
                    v12 = v17[v12];
                }
            }

            int ReadCompressedChunk (int src, byte[] chunk, int chunk_size) // sub_4450E0
            {
                byte[] v33 = new byte[0x10];
                byte[] v35 = new byte[0x10];

                for (byte v6 = 0; v6 < 0x10; ++v6)
                {
                    v33[v6] = v6;
                    v35[v6] = v6;
                }
                int v31 = 0;
                sbyte v5 = -1;
                while ( v31 < chunk_size )
                {
                    int v16;
                    int v26;
                    if (!NextBit())
                    {
                        if (NextBit())
                        {
                            v26 = ReadCount();
                            v16 = v35[v26];
                            chunk[v31++] = (byte)v16;
                        }
                        else
                        {
                            if (NextBit())
                            {
                                int v27 = ReadCount();
                                if (NextBit())
                                    v16 = (v5 - v27) & 0xff;
                                else
                                    v16 = (v5 + v27) & 0xff;
                            }
                            else
                            {
                                v16 = m_input[src++];
                            }
                            chunk[v31++] = (byte)v16;
                            v26 = 0;
                            while (v35[v26] != v16)
                            {
                                ++v26;
                                if (v26 >= 0x10)
                                {
                                    v26 = 0xff;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        int v17;
                        int count = ReadCount();
                        if (NextBit())
                        {
                            v17 = 0;
                            v16 = v33[0];
                        }
                        else if (NextBit())
                        {
                            v17 = ReadCount();
                            v16 = v33[v17];
                        }
                        else
                        {
                            if (NextBit())
                            {
                                int v20 = ReadCount();
                                if (NextBit())
                                    v16 = (v5 - v20) & 0xff;
                                else
                                    v16 = (v5 + v20) & 0xff;
                            }
                            else
                            {
                                v16 = m_input[src++];
                            }
                            v17 = 0;
                            while (v33[v17] != v16)
                            {
                                ++v17;
                                if (v17 >= 0x10)
                                {
                                    v17 = 0xff;
                                    break;
                                }
                            }
                        }
                        if (v17 != 0)
                        {
                            for (int i = v17 & 0xF; i != 0; --i)
                                v33[i] = v33[i-1];
                            v33[0] = (byte)v16;
                        }
                        for (int n = 0; n < count; ++n)
                            chunk[v31++] = (byte)v16;

                        v26 = 0;
                        while (v35[v26] != v16)
                        {
                            ++v26;
                            if (v26 >= 0x10)
                            {
                                v26 = 0xff;
                                break;
                            }
                        }
                    }
                    if (0 != (byte)v26)
                    {
                        for (int k = v26 & 0xF; k != 0; --k)
                            v35[k] = v35[k-1];
                        v35[0] = (byte)v16;
                    }
                    v5 = (sbyte)v16;
                }
                return src;
            }

            int ReadRawChunk (int src, int chunk_size) // sub_445400
            {
                int n = 0;
                while (n < chunk_size)
                {
                    if (!NextBit())
                    {
                        m_output[m_dst++] = m_input[src++];
                        m_output[m_dst++] = m_input[src++];
                        m_output[m_dst++] = m_input[src++];
                        n += 3;
                    }
                    else
                    {
                        int count = ReadCount();
                        byte b = m_input[src++];
                        byte g = m_input[src++];
                        byte r = m_input[src++];
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[m_dst++] = b;
                            m_output[m_dst++] = g;
                            m_output[m_dst++] = r;
                        }
                        n += 3 * count;
                    }
                }
                return src;
            }
        }
    }
}
