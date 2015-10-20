//! \file       ImageMSK.cs
//! \date       Sat Oct 17 09:33:34 2015
//! \brief      Silky's bitmap mask.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Silky
{
    [Export(typeof(ImageFormat))]
    public class RmskFormat : ImageFormat
    {
        public override string         Tag { get { return "RMSK/SILKY'S"; } }
        public override string Description { get { return "Silky's bitmap mask format"; } }
        public override uint     Signature { get { return 0x6B736D52; } } // 'Rmsk'

        public RmskFormat ()
        {
            Extensions = new string[] { "msk" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[12];
            if (12 != stream.Read (header, 0, 12))
                return null;
            return new ImageMetaData
            {
                Width = LittleEndian.ToUInt16 (header, 8),
                Height = LittleEndian.ToUInt16 (header, 10),
                OffsetX = LittleEndian.ToInt16 (header, 4),
                OffsetY = LittleEndian.ToInt16 (header, 6),
                BPP = 8,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var reader = new RmskReader (stream, info))
            {
                reader.Unpack();
                return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, reader.Data, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RmskFormat.Write not implemented");
        }
    }

    internal sealed class RmskReader : IDisposable
    {
        MsbBitStream    m_input;
        byte[]          m_output;
        int             m_flag;
        int             m_width;
        int             m_height;

        public byte[] Data { get { return m_output; } }

        public RmskReader (Stream input, ImageMetaData info)
        {
            input.Position = 0xC;
            m_flag = input.ReadByte();
            if (-1 == m_flag)
                throw new EndOfStreamException();
            input.Position = 0xE;
            m_input = new MsbBitStream (input, true);
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_output = new byte[m_width*m_height];
        }

        public void Unpack ()
        {
            if (0 == (m_flag & 1))
                UnpackV0();
            else
                UnpackV1();
        }

        void UnpackV0 ()
        {
            int line = m_width * (m_height - 1);
            for (int h = 0; h < m_height; ++h)
            {
                int dst = line;
                int column = m_width;
                while (column > 0)
                {
                    if (0 == m_input.GetNextBit())
                    {
                        int src;
                        int count;
                        if (1 == m_input.GetNextBit())
                        {
                            if (0 == m_input.GetNextBit())
                            {
                                src = dst + OffsetTable8[m_input.GetBits (3)];
                            }
                            else
                            {
                                src = m_width + dst + OffsetTable16[m_input.GetBits (4)];
                            }
                        }
                        else
                        {
                            src = dst;
                            if (1 == m_input.GetNextBit())
                            {
                                src += m_width * (m_input.GetNextBit() + 2);
                            }
                            else
                            {
                                src += m_width * (m_input.GetBits (2) + 4);
                            }
                            src += OffsetTable16[m_input.GetBits (4)];
                        }
                        if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetNextBit() + 2;
                        }
                        else if (1 ==  m_input.GetNextBit())
                        {
                            count = m_input.GetBits (2) + 4;
                        }
                        else if (1 ==  m_input.GetNextBit())
                        {
                            count = m_input.GetBits (3) + 8;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (6) + 16;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (8) + 80;
                        }
                        else
                        {
                            count = m_input.GetBits (10) + 336;
                        }
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = m_output[src++];
                        }
                        column -= count;
                    }
                    else
                    {
                        m_output[dst++] = (byte)m_input.GetBits (8);
                        --column;
                    }
                }
                line -= m_width;
            }
        }

        void UnpackV1 ()
        {
            int column = m_width * (m_height - 1);
            for (int x = 0; x < m_width; ++x)
            {
                int dst = column;
                int line = m_height;
                while (line > 0)
                {
                    if (0 != m_input.GetNextBit())
                    {
                        m_output[dst] = (byte)m_input.GetBits (8);
                        dst -= m_width;
                        --line;
                    }
                    else
                    {
                        int src;
                        if (1 == m_input.GetNextBit())
                        {
                            if (0 == m_input.GetNextBit())
                            {
                                src = dst - m_width * OffsetTable8[m_input.GetBits (3)];
                            }
                            else
                            {
                                src = dst - 1 - m_width * OffsetTable16[m_input.GetBits (4)];
                            }
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            int n = dst - 2 - m_input.GetNextBit();
                            src = n - m_width * OffsetTable16[m_input.GetBits (4)];
                        }
                        else
                        {
                            int n = dst - 4 - m_input.GetBits (2);
                            src = n - m_width * OffsetTable16[m_input.GetBits (4)];
                        }
                        int count;
                        if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (1) + 2;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (2) + 4;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (3) + 8;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (6) + 16;
                        }
                        else if (1 == m_input.GetNextBit())
                        {
                            count = m_input.GetBits (8) + 80;
                        }
                        else
                        {
                            count = m_input.GetBits (10) + 336;
                        }
                        line -= count;
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = m_output[src];
                            src -= m_width;
                            dst -= m_width;
                        }
                    }
                }
                ++column;
            }
        }

        static int[] OffsetTable8  = { -1, -2, -4, -6, -8, -12, -16, -20 };
        static int[] OffsetTable16 = { -20, -16, -12, -8, -6, -4, -2, -1, 0, 1, 2, 4, 6, 8, 12, 16 };

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
