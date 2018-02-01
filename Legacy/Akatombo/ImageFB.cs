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
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (6),
                BPP = header.ToUInt16 (2),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new FbReader (file, info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, null, pixels);
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
        int             m_pixel_count;
        byte[]          m_output;

        public PixelFormat Format { get; private set; }

        public FbReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_pixel_count = m_width * (int)info.Height;
            m_output = new byte[m_pixel_count * 4];
            Format = PixelFormats.Bgr32;
        }

        uint    m_bits;

        public byte[] Unpack ()
        {
            m_input.Position = 8;
            m_bits = 0x80000000;
            int dst = 0;
            for (int i = 0; i < m_pixel_count; ++i)
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
                    Buffer.BlockCopy (m_output, pos, m_output, dst, 4);
                }
                m_output[dst  ] += ReadDiff();
                m_output[dst+1] += ReadDiff();
                m_output[dst+2] += ReadDiff();
                dst += 4;
            }
            return m_output;
        }

        byte ReadDiff ()
        {
            uint n = 1;
            do
            {
                n = Binary.RotR (n, 1);
            }
            while (GetNextBit() != 0);
            ++n;
            byte bit;
            do
            {
                bit = (byte)GetNextBit();
                n = (n << 1) | bit;
            }
            while (0 == bit);
            return (byte)(-(n & 1) ^ ((n >> 1) - 1));
        }

        int GetNextBit ()
        {
            uint bit = m_bits >> 31;
            m_bits <<= 1;
            if (0 == m_bits)
            {
                m_bits = Binary.BigEndian (m_input.ReadUInt32());
                bit = m_bits >> 31;
                m_bits = (m_bits << 1) | 1;
            }
            return (int)bit;
        }
    }
}
