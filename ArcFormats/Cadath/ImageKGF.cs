//! \file       ImageKGF.cs
//! \date       Thu Dec 22 16:47:04 2016
//! \brief      Cadath image format.
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

namespace GameRes.Formats.Cadath
{
    internal class KgfMetaData : ImageMetaData
    {
        public int  Mode;
    }

    [Export(typeof(ImageFormat))]
    public class KgfFormat : ImageFormat
    {
        public override string         Tag { get { return "KGF"; } }
        public override string Description { get { return "Cadath image format"; } }
        public override uint     Signature { get { return 0x46474B; } } // 'KGF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            return new KgfMetaData
            {
                Width   = header.ToUInt32 (4),
                Height  = header.ToUInt32 (8),
                BPP     = header.ToInt32 (0xC),
                Mode    = header.ToInt32 (0x10),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new KgfDecoder (file, (KgfMetaData)info);
            decoder.Unpack();
            return ImageData.Create (info, decoder.Format, null, decoder.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("KgfFormat.Write not implemented");
        }
    }

    internal class KgfDecoder
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        int             m_pixel_size;
        int             m_mode;

        public byte[]        Data { get { return m_output; } }
        public PixelFormat Format { get; private set; }

        public KgfDecoder (IBinaryStream input, KgfMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_mode = info.Mode;
            if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
            m_pixel_size = info.BPP / 8;
            m_output = new byte[m_width * m_height * m_pixel_size];
        }

        public void Unpack ()
        {
            switch (m_mode)
            {
            case 5: UnpackV5(); break;
            case 0:
            case 1:
            case 2: throw new NotImplementedException (string.Format ("KGF image type {0} not implemented", m_mode));
            default: throw new InvalidFormatException();
            }
        }

        void UnpackV5 ()
        {
            m_input.Position = 0x24;
            int packed_size = m_input.ReadInt32();
            var data = Decompress (packed_size);
            var line = new byte[m_width];
            int channel_size = m_width * m_height;
            int src = 0;
            for (int i = 0; i < m_pixel_size; ++i)
            {
                for (int j = 0; j < line.Length; ++j)
                    line[j] = 0;
                int dst = i;
                for (int j = 0; j < channel_size; ++j)
                {
                    int pos = j % m_width;
                    byte b = line[pos];
                    b ^= data[src++];
                    m_output[dst] = b;
                    line[pos] = b;
                    dst += m_pixel_size;
                }
            }
        }

        byte[] Decompress (int input_size)
        {
            var output = new byte[m_output.Length];
            var buf0 = new InputBuffer (0x200);
            var buf1 = new InputBuffer (0x200 >> 3);

            var bits_buf = new byte[buf1.Length + 1];
            int dst = 0;
            while (dst < output.Length)
            {
                if (0 == buf1.ReadFrom (m_input))
                    throw new InvalidFormatException();
                int output_chunk_size;
                if (buf1.Decode (bits_buf, 0, buf1.Length, m_input, out output_chunk_size))
                    throw new InvalidFormatException();
                buf0.ReadFrom (bits_buf);
                if (buf0.Decode (output, dst, output.Length - dst, m_input, out output_chunk_size))
                    break;
                dst += output_chunk_size;
            }
            return output;
        }

        class InputBuffer
        {
            byte        m_last_byte;
            byte[]      m_ctl_bits;
            byte[]      m_data;

            public int     Length { get; private set; }
            public int ByteLength { get { return Length >> 3; } }

            public InputBuffer (int length)
            {
                Length = length;
                m_last_byte = 0;
                m_ctl_bits = new byte[(length >> 3) + 1];
                m_data = new byte[length + 1];
            }

            public int ReadFrom (IBinaryStream input)
            {
                return input.Read (m_ctl_bits, 0, ByteLength);
            }

            public int ReadFrom (byte[] input)
            {
                Buffer.BlockCopy (input, 0, m_ctl_bits, 0, ByteLength);
                return ByteLength;
            }

            public bool Decode (byte[] output, int dst_pos, int output_size, IBinaryStream input, out int output_chunk_size)
            {
                for (int i = 0; i < Length; ++i)
                {
                    if (0 != (m_ctl_bits[i >> 3] & 1))
                    {
                        m_data[i] = 0;
                    }
                    else
                    {
                        int next = input.ReadByte();
                        if (-1 == next)
                            break;
                        m_data[i] = (byte)next;
                    }
                    m_ctl_bits[i >> 3] >>= 1;
                }
                m_data[0] ^= m_last_byte;
                for (int i = 0; i < Length; ++i)
                {
                    m_data[i+1] ^= m_data[i];
                }
                m_last_byte = m_data[Length - 1];
                output_chunk_size = Math.Min (Length, output_size);
                Buffer.BlockCopy (m_data, 0, output, dst_pos, output_chunk_size);
                return Length > output_size;
            }
        }
    }
}
