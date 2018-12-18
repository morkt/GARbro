//! \file       ImageFB.cs
//! \date       2018 Jan 25
//! \brief      Akatombo compressed image format.
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

namespace GameRes.Formats.Akatombo
{
    [Export(typeof(ImageFormat))]
    public class FbFormat : ImageFormat
    {
        public override string         Tag { get { return "FB"; } }
        public override string Description { get { return "Akatombo image format"; } }
        public override uint     Signature { get { return 0x184246; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual ("FB"))
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = header[2],
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new FbReader (file, info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FbFormat.Write not implemented");
        }
    }

    internal class FbReader
    {
        IBinaryStream   m_input;
        int             m_width;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }
        public int         Stride { get; private set; }

        public FbReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_width = info.iWidth;
            Stride = 4 * m_width;
            m_output = new byte[Stride * info.iHeight];
            Format = PixelFormats.Bgr32;
        }

        uint    m_bits;

        public byte[] Unpack ()
        {
            m_input.Position = 8;
            m_bits = 0x80000000;
            for (int dst = 0; dst < m_output.Length; dst += 4)
            {
                int bit = GetNextBit();
                if (0 == bit)
                {
                    bit = GetNextBit();
                    int pos = GetNextBit();
                    if (bit != 0)
                        pos = dst + 4 * (pos - m_width);
                    else
                        pos = dst + 4 * (-m_width & -pos) - 4;
                    if (pos >= 0)
                        Buffer.BlockCopy (m_output, pos, m_output, dst, 4);
                }
                m_output[dst  ] += ReadDiff();
                m_output[dst+1] += ReadDiff();
                m_output[dst+2] += ReadDiff();
            }
            return m_output;
        }

        byte ReadDiff ()
        {
            int count = 1;
            while (GetNextBit() != 0)
            {
                ++count;
            }
            int n = 1;
            while (count --> 0)
            {
                n = (n << 1) | GetNextBit();
            }
            return (byte)(-(n & 1) ^ ((n >> 1) - 1));
        }

        byte[] m_bits_buffer = new byte[4];

        int GetNextBit ()
        {
            uint bit = m_bits >> 31;
            m_bits <<= 1;
            if (0 == m_bits)
            {
                if (0 == m_input.Read (m_bits_buffer, 0, 4))
                    throw new EndOfStreamException();
                m_bits = BigEndian.ToUInt32 (m_bits_buffer, 0);
                bit = m_bits >> 31;
                m_bits = (m_bits << 1) | 1;
            }
            return (int)bit;
        }
    }
}
