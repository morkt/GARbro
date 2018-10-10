//! \file       ImageNGC.cs
//! \date       2018 Oct 10
//! \brief      Brownie image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Brownie
{
    internal class NgcMetaData : ImageMetaData
    {
        public int  BitsLineSize;
    }

    [Export(typeof(ImageFormat))]
    public class NgcFormat : ImageFormat
    {
        public override string         Tag { get { return "NGC"; } }
        public override string Description { get { return "Brownie image format"; } }
        public override uint     Signature { get { return 0x422F474E; } } // 'NG/B'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            return new NgcMetaData {
                Width  = header.ToUInt32 (0x14),
                Height = header.ToUInt32 (0x18),
                BPP    = 24,
                BitsLineSize = header.ToInt32 (0x1C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new NgcReader (file, (NgcMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NgcFormat.Write not implemented");
        }
    }

    internal class NgcReader
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        byte[]          m_bits;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public NgcReader (IBinaryStream input, NgcMetaData info)
        {
            m_input = input;
            Stride = 3 * (int)info.Width;
            Format = PixelFormats.Bgr24;
            m_output = new byte[Stride * (int)info.Height];
            m_bits = new byte[info.BitsLineSize];
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x20;
            int dst = 0;
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                else if (2 == ctl)
                {
                    ReadBitsLine (dst);
                }
                else if (3 == ctl)
                {
                    ReadRleLine (dst);
                    ReadRleLine (dst+1);
                    ReadRleLine (dst+2);
                }
                else if (0 == ctl)
                {
                    Binary.CopyOverlapped (m_output, dst - Stride, dst, Stride);
                }
                else
                {
                    m_input.Read (m_output, dst, Stride);
                }
                dst += Stride;
            }
            return m_output;
        }

        void ReadRleLine (int dst)
        {
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadUInt8();
                if (ctl != 0)
                {
                    int count = ctl;
                    byte v = m_input.ReadUInt8();
                    while (count --> 0)
                    {
                        m_output[dst] = v;
                        dst += 3;
                    }
                }
                else
                {
                    int count = m_input.ReadUInt8();
                    if (0 == count)
                        break;
                    while (count --> 0)
                    {
                        m_output[dst] = m_input.ReadUInt8();
                        dst += 3;
                    }
                }
            }
        }

        int ReadBitsLine (int dst)
        {
            m_input.Read (m_bits, 0, m_bits.Length);
            int bsrc = 0;
            int count = Stride;
            while (bsrc < m_bits.Length && count > 0)
            {
                for (byte mask = 0x80; mask != 0 && count > 0; mask >>= 1)
                {
                    if ((m_bits[bsrc] & mask) != 0)
                    {
                        m_output[dst] = m_input.ReadUInt8();
                    }
                    else
                    {
                        m_output[dst] = m_output[dst - Stride];
                    }
                    dst++;
                    --count;
                }
                ++bsrc;
            }
            return dst;
        }
    }
}
