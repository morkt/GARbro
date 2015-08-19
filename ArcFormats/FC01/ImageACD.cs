//! \file       ImageACD.cs
//! \date       Mon Jul 13 16:13:36 2015
//! \brief      F&C Co. image format.
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
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.FC01
{
    internal class AcdMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class AcdFormat : ImageFormat
    {
        public override string         Tag { get { return "ACD"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return 0x20444341; } } // 'ACD'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x1c];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int header_size = LittleEndian.ToInt32 (header, 8);
            if (!Binary.AsciiEqual (header, 4, "1.00") || header_size < 0x1c)
                throw new NotSupportedException ("Not supported ACD image version");
            int packed_size = LittleEndian.ToInt32 (header, 0x0C);
            int unpacked_size = LittleEndian.ToInt32 (header, 0x10);
            return new AcdMetaData
            {
                Width = LittleEndian.ToUInt32 (header, 0x14),
                Height = LittleEndian.ToUInt32 (header, 0x18),
                BPP = 24,
                DataOffset = header_size,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as AcdMetaData;
            if (null == meta)
                throw new ArgumentException ("AcdFormat.Read should be supplied with AcdMetaData", "info");

            stream.Position = meta.DataOffset;
            using (var reader = new MrgLzssReader (stream, meta.PackedSize, meta.UnpackedSize))
            {
                reader.Unpack();
                var decoder = new AcdDecoder (reader.Data, meta);
                decoder.Unpack();
                return ImageData.Create (info, PixelFormats.Gray8, null, decoder.Data);
            }
            throw new InvalidFormatException();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AcdFormat.Write not implemented");
        }
    }

    internal class AcdDecoder
    {
        byte[]          m_input;
        byte[]          m_output;

        public byte[] Data { get { return m_output; } }

        public AcdDecoder (byte[] input, AcdMetaData info)
        {
            m_input = input;
            m_output = new byte[info.Width*info.Height];
        }

        int m_src;
        int m_bits;

        public byte[] Unpack ()
        {
            m_src = 0; // @@SB
            m_bits = 0;
            for (int dst = 0; dst < m_output.Length; dst++)
            {
                int pixel = 0;
                if (0 != GetBit())
                {
                    --pixel;
                    if (0 == GetBit())
                    {
                        pixel += 3;
                        int bit;
                        do
                        {
                            bit = GetBit();
                            pixel += pixel + bit;
                            bit = (pixel >> 8) & 1;
                            pixel &= 0xff;
                        }
                        while (0 == bit);
                        if (0 != pixel)
                        {
                            ++pixel;
                            pixel *= 0x28CCCCD;
                            pixel = (int)((uint)pixel >> 24);
                        }
                    }
                }
                m_output[dst] = (byte)pixel;
            }
            return m_output;
        }

        int GetBit ()
        {
            int bit = m_bits >> 7;
            m_bits = (m_bits << 1) & 0xff;
            if (0 == m_bits)
            {
                if (m_src >= m_input.Length)
                    throw new InvalidFormatException();
                m_bits = m_input[m_src++];
                bit = m_bits >> 7;
                m_bits = (m_bits << 1) & 0xff | 1;
            }
            return bit;
        }
    }
}
