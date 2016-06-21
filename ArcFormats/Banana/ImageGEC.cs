//! \file       ImageGEC.cs
//! \date       Mon Jun 20 15:45:46 2016
//! \brief      Yellow Pig image format.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.YellowPig
{
    internal class GecMetaData : ImageMetaData
    {
        public byte Type;
        public int  DataOffset;
        public int  AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GecFormat : ImageFormat
    {
        public override string         Tag { get { return "GEC"; } }
        public override string Description { get { return "Yellow Pig image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x11];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            byte type = header[0];
            if (type != 0 && type != 1)
                return null;
            var info = new GecMetaData
            {
                Type    = type,
                OffsetX = LittleEndian.ToInt16 (header, 1),
                OffsetY = LittleEndian.ToInt16 (header, 3),
                Width   = LittleEndian.ToUInt16 (header, 5),
                Height  = LittleEndian.ToUInt16 (header, 7),
                BPP     = 0 == type ? 24 : 32,
                AlphaOffset = LittleEndian.ToInt32 (header, 9),
                DataOffset  = LittleEndian.ToInt32 (header, 0xD),
            };
            if (info.OffsetX < 0 || info.OffsetY < 0 || info.Width <= 0 || info.Height <= 0
                || info.DataOffset < 0 || info.DataOffset > stream.Length)
                return null;
            if (1 == type && info.AlphaOffset <= 0)
                return null;
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new GecReader (stream, (GecMetaData)info);
            reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, null, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GecFormat.Write not implemented");
        }
    }

    internal sealed class GecReader
    {
        byte[]          m_input;
        byte[]          m_output;
        GecMetaData     m_info;

        public PixelFormat  Format { get; private set; }
        public int          Stride { get; private set; }
        public byte[]         Data { get { return m_output; } }

        public GecReader (Stream input, GecMetaData info)
        {
            m_input = new byte[input.Length];
            input.Read (m_input, 0, m_input.Length);
            m_info = info;
        }

        int m_bits = 0;
        int m_bits_src = 0;
        int m_bits_count = 0;

        public void Unpack ()
        {
            if (0 == m_info.Type)
            {
                UnpackPixels (0x11);
                Format = PixelFormats.Bgr24;
                Stride = (int)m_info.Width * 3;
            }
            else
            {
                UnpackPixels (0x1D);
                int bits = 0x1D + m_info.AlphaOffset;
                m_alpha_width  = LittleEndian.ToUInt16 (m_input, 0x15);
                m_alpha_height = LittleEndian.ToUInt16 (m_input, 0x17);
                int data = bits + LittleEndian.ToInt32 (m_input, 0x19);
                var alpha = UnpackAlpha (bits, data);
                ApplyAlpha (alpha);
                Format = PixelFormats.Bgra32;
                Stride = (int)m_info.Width * 4;
            }
        }

        int m_alpha_width;
        int m_alpha_height;

        void ApplyAlpha (byte[] alpha)
        {
            var image = new byte[m_info.Width * m_info.Height * 4];
            int src = 0;
            int a_y = m_alpha_height - (int)m_info.Height - m_info.OffsetY;
            int a_src = a_y * m_alpha_width + m_info.OffsetX;
            int dst = 0;
            for (uint y = 0; y < m_info.Height; ++y)
            {
                for (uint x = 0; x < m_info.Width; ++x)
                {
                    image[dst++] = m_output[src++];
                    image[dst++] = m_output[src++];
                    image[dst++] = m_output[src++];
                    image[dst++] = alpha[a_src+x];
                }
                a_src += m_alpha_width;
            }
            m_output = image;
        }

        int m_dst;
        byte[] m_table = new byte[0x100];

        void UnpackPixels (int bits_src)
        {
            m_bits_src = bits_src;
            m_bits_count = 0;
            m_output = new byte[(int)m_info.Width * (int)m_info.Height * 3];
            int src = bits_src + m_info.DataOffset;
            for (int j = 0; j < 0x100; ++j)
                m_table[j] = (byte)j;
            byte[] frame1 = new byte[0x10002];
            byte[] frame2 = new byte[0x10002];
            m_dst = 0;
            while (m_dst < m_output.Length)
            {
                int count = Math.Min (m_output.Length - m_dst, 0xFFFF);
                if (GetNextBit() != 0)
                {
                    ReadFrame (frame1, count + 2);
                    UnpackFrame1 (frame1, frame2, count + 2);
                    UnpackFrame2 (frame2, 2, count, LittleEndian.ToUInt16 (frame2, 0));
                }
                else
                {
                    src = UnpackRLE (src, count);
                }
            }
        }

        byte[] UnpackAlpha (int bits_src, int data_src)
        {
            m_bits_src = bits_src;
            m_bits_count = 0;
            var alpha = new byte[m_alpha_height * m_alpha_width];
            int dst = 0;
            while (dst < alpha.Length)
            {
                if (GetNextBit() != 0)
                {
                    int count = GetInt();
                    byte v = m_input[data_src++];
                    while (count --> 0)
                        alpha[dst++] = v;
                }
                else
                {
                    alpha[dst++] = m_input[data_src++];
                }
            }
            return alpha;
        }

        void ReadFrame (byte[] frame, int count) // sub_423990
        {
            int j = 0;
            while (j < count)
            {
                if (0 != GetNextBit())
                {
                    frame[j++] = (byte)GetInt();
                }
                else
                {
                    int n = GetInt();
                    while (n --> 0)
                        frame[j++] = 0;
                }
            }
        }

        void UnpackFrame1 (byte[] frame, byte[] dst, int count) // sub_423670
        {
            byte prev = 1;
            int n = 0;
            while (count --> 0)
            {
                byte v8 = frame[n];
                byte v9 = m_table[v8];
                if (1 == v8)
                {
                    if (prev != 0)
                    {
                        m_table[1] = m_table[0];
                        m_table[0] = v9;
                    }
                }
                else if (v8 > 1)
                {
                    Buffer.BlockCopy (m_table, 1, m_table, 2, v8 - 1);
                    m_table[1] = v9;
                }
                dst[n++] = v9;
                prev = v8;
            }
        }

        ushort[] table1 = new ushort[0x100];
        ushort[] table2 = new ushort[0x100];
        ushort[] table3 = new ushort[0x10002];

        void UnpackFrame2 (byte[] frame, int src, int count, ushort first) // sub_4236F0
        {
            for (int i = 0; i < 0x100; ++i)
                table1[i] = 0;
            for (int i = 0; i < count; ++i)
                ++table1[frame[src+i]];
            ushort v = 0;
            for (int i = 0; i < 0x100; i += 1)
            {
                table2[i] = v;
                v += table1[i];
                table1[i] = 0;
            }
            for (int i = 0; i < count; ++i)
            {
                int d = frame[src+i];
                ushort a = table2[d];
                ushort b = table1[d]++;
                table3[a + b] = (ushort)i;
            }
            ushort next = table3[first];
            while (count --> 0)
            {
                m_output[m_dst++] = frame[src+next];
                next = table3[next];
            }
        }

        int UnpackRLE (int src, int count) // sub_423A10
        {
            while (count > 0)
            {
                if (0 == GetNextBit())
                {
                    m_output[m_dst++] = m_input[src++];
                    m_output[m_dst++] = m_input[src++];
                    m_output[m_dst++] = m_input[src++];
                    count -= 3;
                }
                else
                {
                    int n = GetInt() * 3;
                    m_output[m_dst]   = m_input[src++];
                    m_output[m_dst+1] = m_input[src++];
                    m_output[m_dst+2] = m_input[src++];
                    Binary.CopyOverlapped (m_output, m_dst, m_dst+3, n-3);
                    m_dst += n;
                    count -= n;
                }
            }
            return src;
        }

        int GetInt () // sub_423810
        {
            int count = 0;
            while (0 == GetNextBit())
            {
                ++count;
            }
            int v = 1;
            while (count > 0)
            {
                v = (v << 1) | GetNextBit();
                --count;
            }
            return v;
        }

        int GetNextBit ()
        {
            if (m_bits_count-- <= 0)
            {
                m_bits = LittleEndian.ToInt32 (m_input, m_bits_src);
                m_bits_src += 4;
                m_bits_count = 31;
            }
            else
            {
                m_bits >>= 1;
            }
            return m_bits & 1;
        }
    }
}
