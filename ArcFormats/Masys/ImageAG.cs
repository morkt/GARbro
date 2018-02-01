//! \file       ImageAG.cs
//! \date       Sun May 10 23:53:34 2015
//! \brief      Masys Enhanced Game Unit image format.
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
using System.Windows.Media;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Megu
{
    [Export(typeof(ImageFormat))]
    public class AgFormat : ImageFormat
    {
        public override string         Tag { get { return "ACG"; } }
        public override string Description { get { return "Masys image format"; } }
        public override uint     Signature { get { return 0x00644741u; } } // 'AGd'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            var info = new ImageMetaData();
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            file.Position = 0x38;
            int alpha_size = file.ReadInt32();
            info.BPP = 0 == alpha_size ? 24 : 32;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new AgReader (stream, info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("AgFormat.Write not implemented");
        }
    }

    internal class AgReader
    {
        byte[] in1;
        byte[] in2;
        byte[] in3;
        byte[] in4;
        byte[] in5;
        byte[] m_alpha;
        byte[] m_output;
        int m_width;
        int m_height;
        int m_pixel_size;
        byte[] m_first = new byte[3];

        public byte[] Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public AgReader (IBinaryStream input, ImageMetaData info)
        {
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            input.Position = 0x0c;
            uint offset1 = input.ReadUInt32();
            int  size1   = input.ReadInt32();
            uint offset2 = input.ReadUInt32();
            int  size2   = input.ReadInt32();
            uint offset3 = input.ReadUInt32();
            int  size3   = input.ReadInt32();
            uint offset4 = input.ReadUInt32();
            int  size4   = input.ReadInt32();
            uint offset5 = input.ReadUInt32();
            int  size5   = input.ReadInt32();
            uint offset6 = input.ReadUInt32();
            int  size6   = input.ReadInt32();
            input.Read (m_first, 0, 3);
            if (size1 != 0)
                in1 = ReadSection (input, offset1, size1);
            if (size2 != 0)
                in2 = ReadSection (input, offset2, size2);
            if (size3 != 0)
                in3 = ReadSection (input, offset3, size3);
            if (size4 != 0)
                in4 = ReadSection (input, offset4, size4);
            if (size5 != 0)
                in5 = ReadSection (input, offset5, size5);
            if (size6 != 0)
            {
                input.Position = offset6;
                m_alpha = new byte[m_height*m_width];
                RleDecode (input, m_alpha);
                Format = PixelFormats.Bgra32;
                m_pixel_size = 4;
            }
            else
            {
                Format = PixelFormats.Bgr24;
                m_pixel_size = 3;
            }
            m_output = new byte[m_width*m_height*m_pixel_size];
        }

        static private byte[] ReadSection (IBinaryStream input, long offset, int size)
        {
            input.Position = offset;
            var buf = new byte[size + 4];
            if (size != input.Read (buf, 0, size))
                throw new InvalidFormatException ("Unexpected end of file");
            return buf;
        }

        public void Unpack ()
        {
            int bit_mask = 1;
            int src1 = 0;
            uint v25 = LittleEndian.ToUInt32 (in1, src1);
            src1 += 4;
            int v5 = 1;
            int src2 = 0;
            uint v24 = LittleEndian.ToUInt32 (in2, src2);
            src2 += 4;
            int v4 = 1;
            int src3 = 0;
            uint v23 = LittleEndian.ToUInt32 (in3, src3);
            src3 += 4;
            int v3 = 0;
            int src4 = 0;
            uint v22 = LittleEndian.ToUInt32 (in4, src4);
            src4 += 4;
            int v19 = 0;
            int src5 = 0;
            uint v21 = LittleEndian.ToUInt32 (in5, src5);
            src5 += 4;

            byte B;
            byte G;
            byte R;

            int stride = m_width * m_pixel_size;
            for (int y = 0; y < m_height; ++y)
            {
                int dst = y * stride;
                for (int x = 0; x < stride; x += m_pixel_size)
                {
                    if (0 != (bit_mask & v25))
                    {
                        B = (byte)(v21 >> v19);
                        v19 += 8;
                        if (32 == v19)
                        {
                            v21 = LittleEndian.ToUInt32 (in5, src5);
                            src5 += 4;
                            v19 = 0;
                        }
                    }
                    else
                    {
                        if (0 != (v4 & v23))
                        {
                            B = m_first[0];
                        }
                        else
                        {
                            B = (byte)((v22 >> v3) & 0xF);
                            v3 += 4;
                            if (32 == v3)
                            {
                                v22 = LittleEndian.ToUInt32 (in4, src4);
                                src4 += 4;
                                v3 = 0;
                            }
                            ++B;
                            if (0 != (v5 & v24))
                                B = (byte)(m_first[0] - B);
                            else
                                B += m_first[0];
                            v5 <<= 1;
                            if (0 == v5)
                            {
                                v5 = 1;
                                v24 = LittleEndian.ToUInt32 (in2, src2);
                                src2 += 4;
                            }
                        }
                        v4 <<= 1;
                        if (0 == v4)
                        {
                            v4 = 1;
                            v23 = LittleEndian.ToUInt32 (in3, src3);
                            src3 += 4;
                        }
                    }
                    bit_mask <<= 1;
                    if (0 == bit_mask)
                    {
                        v25 = LittleEndian.ToUInt32 (in1, src1);
                        src1 += 4;
                        bit_mask = 1;
                    }
                    if (0 != (bit_mask & v25))
                    {
                        G = (byte)(v21 >> v19);
                        v19 += 8;
                        if (32 == v19)
                        {
                            v21 = LittleEndian.ToUInt32 (in5, src5);
                            src5 += 4;
                            v19 = 0;
                        }
                    }
                    else
                    {
                        if (0 != (v4 & v23))
                        {
                            G = m_first[1];
                        }
                        else
                        {
                            G = (byte)((v22 >> v3) & 0xF);
                            v3 += 4;
                            if (32 == v3)
                            {
                                v22 = LittleEndian.ToUInt32 (in4, src4);
                                src4 += 4;
                                v3 = 0;
                            }
                            ++G;
                            if (0 != (v5 & v24))
                                G = (byte)(m_first[1] - G);
                            else
                                G += m_first[1];
                            v5 <<= 1;
                            if (0 == v5)
                            {
                                v5 = 1;
                                v24 = LittleEndian.ToUInt32 (in2, src2);
                                src2 += 4;
                            }
                        }
                        v4 <<= 1;
                        if (0 == v4)
                        {
                            v4 = 1;
                            v23 = LittleEndian.ToUInt32 (in3, src3);
                            src3 += 4;
                        }
                    }
                    bit_mask <<= 1;
                    if (0 == bit_mask)
                    {
                        v25 = LittleEndian.ToUInt32 (in1, src1);
                        src1 += 4;
                        bit_mask = 1;
                    }
                    if (0 != (bit_mask & v25))
                    {
                        R = (byte)(v21 >> v19);
                        v19 += 8;
                        if (32 == v19)
                        {
                            v21 = LittleEndian.ToUInt32 (in5, src5);
                            src5 += 4;
                            v19 = 0;
                        }
                    }
                    else
                    {
                        if (0 != (v4 & v23))
                        {
                            R = m_first[2];
                        }
                        else
                        {
                            R = (byte)((v22 >> v3) & 0xF);
                            v3 += 4;
                            if (32 == v3)
                            {
                                v22 = LittleEndian.ToUInt32 (in4, src4);
                                src4 += 4;
                                v3 = 0;
                            }
                            ++R;
                            if (0 != (v5 & v24))
                                R = (byte)(m_first[2] - R);
                            else
                                R += m_first[2];
                            v5 <<= 1;
                            if (0 == v5)
                            {
                                v5 = 1;
                                v24 = LittleEndian.ToUInt32 (in2, src2);
                                src2 += 4;
                            }
                        }
                        v4 <<= 1;
                        if (0 == v4)
                        {
                            v4 = 1;
                            v23 = LittleEndian.ToUInt32 (in3, src3);
                            src3 += 4;
                        }
                    }
                    bit_mask <<= 1;
                    if (0 == bit_mask)
                    {
                        v25 = LittleEndian.ToUInt32 (in1, src1);
                        src1 += 4;
                        bit_mask = 1;
                    }
                    m_output[dst+x] = B;
                    m_output[dst+x+1] = G;
                    m_output[dst+x+2] = R;
                    m_first[0] = B;
                    m_first[1] = G;
                    m_first[2] = R;
                }
                m_first[0] = m_output[dst];
                m_first[1] = m_output[dst+1];
                m_first[2] = m_output[dst+2];
            }
            if (m_alpha != null)
                ApplyAlpha();
        }

        private void ApplyAlpha ()
        {
            int src = 0;
            for (int i = 3; i < m_output.Length; i += 4)
            {
                int alpha = Math.Min (m_alpha[src++]*0xff/0x40, 0xff);
                m_output[i] = (byte)alpha;
            }
        }

        private static void RleDecode (IBinaryStream src, byte[] dst_buf)
        {
            int remaining = dst_buf.Length;
            int dst = 0;
            while (remaining > 0)
            {
                byte v = src.ReadUInt8();
                int count;
                if (0 != (v & 0x80))
                {
                    v &= 0x7F;
                    count = src.ReadUInt16();
                    for (int j = 0; j < count; ++j)
                    {
                        dst_buf[dst++] = v;
                    }
                }
                else
                {
                    dst_buf[dst++] = v;
                    count = 1;
                }
                remaining -= count;
            }
        }
    }
}
