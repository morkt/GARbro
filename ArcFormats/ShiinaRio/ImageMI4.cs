//! \file       ImageMI4.cs
//! \date       Sun Jul 12 15:40:39 2015
//! \brief      ShiinaRio engine image format.
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

namespace GameRes.Formats.ShiinaRio
{
    [Export(typeof(ImageFormat))]
    public class Mi4Format : ImageFormat
    {
        public override string         Tag { get { return "MI4"; } }
        public override string Description { get { return "ShiinaRio image format"; } }
        public override uint     Signature { get { return 0x3449414D; } } // 'MAI4'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 8;
            uint width  = file.ReadUInt32();
            uint height = file.ReadUInt32();
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x10;
            using (var reader = new Reader (stream, (int)info.Width, (int)info.Height))
            {
                reader.Unpack ();
                return ImageData.Create (info, PixelFormats.Bgr24, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Mi4Format.Write not implemented");
        }

        internal sealed class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            int             m_stride;

            public byte[] Data { get { return m_output; } }

            public Reader (IBinaryStream file, int width, int height)
            {
                m_input = file;
                m_stride = width * 3;
                m_output = new byte[m_stride*height];
            }

            uint m_bit_count;
            uint m_bits;

            void ResetBits ()
            {
                m_bits = m_input.ReadUInt32();
                m_bit_count = 32;
            }

            uint GetBit ()
            {
                uint bit = m_bits >> 31;
                m_bits <<= 1;
                if (0 == --m_bit_count)
                {
                    m_bits = m_input.ReadUInt32();
                    m_bit_count = 32;
                }
                return bit;
            }

            uint GetBits (int count)
            {
                uint bits = 0;
                while (count --> 0)
                {
                    bits = (bits << 1) | GetBit();
                }
                return bits;
            }

            public void Unpack ()
            {
                ResetBits();
                int dst = 0;
                byte b = 0, g = 0, r = 0;
                while (dst < m_output.Length)
                {
                    if (GetBit() == 0)
                    {
                        if (GetBit() != 0)
                        {
                            b = m_input.ReadUInt8();
                            g = m_input.ReadUInt8();
                            r = m_input.ReadUInt8();
                        }
                        else if (GetBit() != 0)
                        {
                            byte v = (byte)(GetBits(2));
                            if (3 == v)
                            {
                                b = m_output[dst - m_stride];
                                g = m_output[dst - m_stride + 1];
                                r = m_output[dst - m_stride + 2];
                            }
                            else
                            {
                                b += (byte)(v - 1);
                                v = (byte)GetBits (2);
                                if (3 == v)
                                {
                                    if (GetBit() != 0)
                                    {
                                        b = m_output[dst - m_stride - 3];
                                        g = m_output[dst - m_stride - 2];
                                        r = m_output[dst - m_stride - 1];
                                    }
                                    else
                                    {
                                        b = m_output[dst - m_stride + 3];
                                        g = m_output[dst - m_stride + 4];
                                        r = m_output[dst - m_stride + 5];
                                    }
                                }
                                else
                                {
                                    g += (byte)(v - 1);
                                    r += (byte)(GetBits(2) - 1);
                                }
                            }
                        }
                        else if (GetBit() != 0)
                        {
                            byte v = (byte)(GetBits(3));
                            if (7 == v)
                            {
                                b = m_output[dst - m_stride];
                                g = m_output[dst - m_stride + 1];
                                r = m_output[dst - m_stride + 2];
                                b += (byte)(GetBits(3) - 3);
                                g += (byte)(GetBits(3) - 3);
                                r += (byte)(GetBits(3) - 3);
                            }
                            else
                            {
                                b += (byte)(v - 3);
                                g += (byte)(GetBits(3) - 3);
                                r += (byte)(GetBits(3) - 3);
                            }
                        }
                        else if (GetBit() != 0)
                        {
                            b += (byte)(GetBits(4) - 7);
                            g += (byte)(GetBits(4) - 7);
                            r += (byte)(GetBits(4) - 7);
                        }
                        else
                        {
                            b += (byte)(GetBits(5) - 15);
                            g += (byte)(GetBits(5) - 15);
                            r += (byte)(GetBits(5) - 15);
                        }
                    }
                    m_output[dst++] = b;
                    m_output[dst++] = g;
                    m_output[dst++] = r;
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
