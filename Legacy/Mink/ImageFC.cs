//! \file       ImageFC.cs
//! \date       2018 Nov 17
//! \brief      Mink compressed bitmap format.
//
// Copyright (C) 2018-2019 by morkt
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

namespace GameRes.Formats.Mink
{
    internal class FcMetaData : ImageMetaData
    {
        public byte Flag;
    }

    [Export(typeof(ImageFormat))]
    public class FcFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/FC"; } }
        public override string Description { get { return "Mink compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public FcFormat ()
        {
            Signatures = new uint[] { 0x01184346, 0x00184346, 0x00204346, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual ("FC"))
                return null;
            int bpp = header[2];
            if (bpp != 24 && bpp != 32)
                return null;
            byte flag = header[3];
            if (flag != 0 && flag != 1)
                return null;
            return new FcMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = bpp,
                Flag   = flag,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new FcReader (file, (FcMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FcFormat.Write not implemented");
        }
    }

    internal class FcReader
    {
        IBinaryStream   m_input;
        FcMetaData      m_info;

        public FcReader (IBinaryStream input, FcMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 8;
            var output = new uint[m_info.iWidth * m_info.iHeight];
            UnpackRgb (output);
            if (32 == m_info.BPP)
                UnpackAlpha (output);
            if (m_info.Flag != 0)
                RestoreRgb (output);
            PixelFormat format = 32 == m_info.BPP ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
            return ImageData.CreateFlipped (m_info, format, null, output, m_info.iWidth * 4);
        }

        void UnpackRgb (uint[] output)
        {
            int dst = 0;
            m_bits = 0x80000000;
            uint ref_pixel = m_info.Flag != 0 ? 0x808080u : 0u;
            uint pixel = ref_pixel;
            while (dst < output.Length)
            {
                if (GetNextBit() == 0)
                {
                    if (GetNextBit() != 0)
                    {
                        int offset = m_info.iWidth + GetNextBit();
                        pixel = output[dst - offset + 1];
                    }
                    else
                    {
                        pixel = output[dst - 1];
                    }
                    uint v = GetVarInt();
                    v = (v >> 1) ^ (uint)-(v & 1);
                    pixel = Binary.RotR (pixel, 8) + (v << 24);

                    v = GetVarInt();
                    v = (v >> 1) ^ (uint)-(v & 1);
                    pixel = Binary.RotR (pixel, 8) + (v << 24);

                    v = GetVarInt();
                    v = (v >> 1) ^ (uint)-(v & 1);
                    pixel = Binary.RotR (pixel, 8) + (v << 24);

                    pixel >>= 8;
                    output[dst++] = pixel;
                }
                else if (GetNextBit() == 0)
                {
                    if (GetNextBit() == 0)
                    {
                        pixel = output[dst - m_info.iWidth - 1];

                        uint v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel = Binary.RotR (pixel, 8) + (v << 24);

                        v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel = Binary.RotR (pixel, 8) + (v << 24);

                        v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel = Binary.RotR (pixel, 8) + (v << 24);

                        pixel >>= 8;
                        output[dst++] = pixel;
                    }
                    else
                    {
                        pixel = ref_pixel;

                        uint v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel ^= v & 0xFF;

                        v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel ^= (v & 0xFF) << 8;

                        v = GetVarInt();
                        v = (v >> 1) ^ (uint)-(v & 1);
                        pixel ^= (v & 0xFF) << 16;

                        output[dst++] = pixel;
                    }
                }
                else if (GetNextBit() != 0)
                {
                    int offset = m_info.iWidth;
                    if (GetNextBit() != 0)
                    {
                        offset += GetNextBit();
                    }
                    else
                    {
                        offset -= 1;
                    }
                    pixel = output[dst - offset];
                    output[dst++] = pixel;
                }
                else if (GetNextBit() == 0)
                {
                    pixel = GetBits (24);
                    output[dst++] = pixel;
                }
                else
                {
                    int count = Math.Min ((int)GetVarInt(), output.Length - dst);
                    while (count --> 0)
                    {
                        output[dst++] = pixel;
                    }
                }
            }
        }

        void UnpackAlpha (uint[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                uint val = GetBits (8) << 24;
                uint count = GetVarInt() + 1;
                while (count != 0)
                {
                    output[dst++] |= val;
                    --count;
                }
            }
        }

        void RestoreRgb (uint[] output)
        {
            int stride = m_info.iWidth;
            int dst = stride;
            while (dst < output.Length)
            {
                uint prev = output[dst-stride];
                uint pixel = output[dst];
                output[dst] =  ((pixel        +  prev        - 0x80) & 0xFF)
                            | (((pixel >>  8) + (prev >>  8) - 0x80) & 0xFF) << 8
                            | (((pixel >> 16) + (prev >> 16) - 0x80) & 0xFF) << 16
                            | pixel & 0xFF000000;
                ++dst;
            }
        }

        uint GetBits (int count)
        {
            uint val = m_bits >> (32 - count);
            m_bits <<= count;
            if (0 == m_bits)
            {
                m_bits = ReadUInt32();
                int shift = BitScanForward (val);
                uint ebx = m_bits ^ 0x80000000;
                m_bits = (m_bits << 1 | 1) << shift;
                val ^= ebx >> (shift ^ 0x1F);
            }
            return val;
        }

        uint    m_bits;

        int GetNextBit ()
        {
            uint bit = m_bits >> 31;
            m_bits <<= 1;
            if (0 == m_bits)
            {
                m_bits = ReadUInt32();
                bit = m_bits >> 31;
                m_bits = m_bits << 1 | 1;
            }
            return (int)bit;
        }

        uint GetVarInt ()
        {
            int count = 0;
            do
            {
                ++count;
            }
            while (GetNextBit() != 0);
            uint num = 1;
            while (count --> 0)
            {
                num = num << 1 | (uint)GetNextBit();
            }
            return num - 2;
        }

        byte[]  m_dword_buffer = new byte[4];

        uint ReadUInt32 ()
        {
            int last = m_input.Read (m_dword_buffer, 0, 4);
            while (last < m_dword_buffer.Length)
                m_dword_buffer[last++] = 0;
            return BigEndian.ToUInt32 (m_dword_buffer, 0);
        }

        static int BitScanForward (uint val)
        {
            int count = 0;
            for (uint mask = 1; mask != 0; mask <<= 1)
            {
                if ((val & mask) != 0)
                    break;
                ++count;
            }
            return count;
        }
    }
}
