//! \file       ImageQNT.cs
//! \date       Thu Apr 09 21:37:18 2015
//! \brief      AliceSoft RGB image format.
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    public class QntMetaData : ImageMetaData
    {
        public uint RGBSize;
        public uint AlphaSize;
    }

    [Export(typeof(ImageFormat))]
    public class QntFormat : ImageFormat
    {
        public override string         Tag { get { return "QNT"; } }
        public override string Description { get { return "AliceSoft System image format"; } }
        public override uint     Signature { get { return 0x544e51; } } // 'QNT'

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("QntFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x44];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int version = LittleEndian.ToInt32 (header, 4);
            if (version <= 0 || version > 2)
                return null;
            if (0x44 != LittleEndian.ToUInt32 (header, 8))
                return null;
            uint width = LittleEndian.ToUInt32 (header, 0x14);
            uint height = LittleEndian.ToUInt32 (header, 0x18);
            if (0 == width || 0 == height)
                return null;
            return new QntMetaData
            {
                Width = width,
                Height = height,
                OffsetX = LittleEndian.ToInt32 (header, 0x0c),
                OffsetY = LittleEndian.ToInt32 (header, 0x10),
                BPP = LittleEndian.ToInt32 (header, 0x1c),
                RGBSize = LittleEndian.ToUInt32 (header, 0x24),
                AlphaSize = LittleEndian.ToUInt32 (header, 0x28),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as QntMetaData;
            if (null == meta)
                throw new ArgumentException ("QntFormat.Read should be supplied with QntMetaData", "info");
            stream.Position = 0x44;
            using (var reader = new Reader (stream, meta))
            {
                reader.Unpack();
                int stride = (int)info.Width * (reader.BPP / 8);
                PixelFormat format = 24 == reader.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
                return ImageData.Create (info, format, null, reader.Data, stride);
            }
        }

        internal class Reader : IDisposable
        {
            byte[]  m_input;
            byte[]  m_alpha;
            byte[]  m_output;
            int     m_bpp;
            int     m_width;
            int     m_height;

            public byte[] Data { get { return m_output; } }
            public int     BPP { get { return m_bpp*8; } }

            public Reader (Stream stream, QntMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                int w = (m_width + 1) & ~1;
                int h = (m_height + 1) & ~1;
                int rgb_size = h * w * 3;
                m_bpp = info.AlphaSize != 0 ? 4 : 3;
                m_input = new byte[rgb_size];
                var alpha_pos = stream.Position + info.RGBSize;
                using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
                    if (rgb_size != zstream.Read (m_input, 0, rgb_size))
                        throw new InvalidFormatException ("Unexpected end of file");
                if (info.AlphaSize != 0)
                {
                    int alpha_size = w * m_height;
                    m_alpha = new byte[alpha_size];
                    stream.Position = alpha_pos;
                    using (var zstream = new ZLibStream (stream, CompressionMode.Decompress, true))
                        if (alpha_size != zstream.Read (m_alpha, 0, alpha_size))
                            throw new InvalidFormatException ("Unexpected end of file");
                }
                m_output = new byte[info.Width*info.Height*m_bpp];
            }

            public void Unpack ()
            {
                int src = 0;
                int dst;
                int stride = m_bpp * m_width;
                for (int channel = 0; channel < 3; ++channel)
                {
                    dst = channel;
                    for (int y = m_height >> 1; y != 0; --y)
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_input[src++];
                            m_output[dst+stride] = m_input[src++];
                            dst += m_bpp;
                        }
                        dst += stride;
                        src += 2 * (m_width & 1);
                    }
                    if (0 != (m_height & 1))
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_input[src];
                            src += 2;
                            dst += m_bpp;
                        }
                        src += 2 * (m_width & 1);
                    }
                }
                if (3 != m_bpp)
                {
                    src = 0;
                    dst = 3;
                    for (int y = 0; y < m_height; ++y)
                    {
                        for (int x = 0; x < m_width; ++x)
                        {
                            m_output[dst] = m_alpha[src++];
                            dst += 4;
                        }
                        src += m_width & 1;
                    }
                }
                dst = m_bpp;
                int i;
                for (i = stride-m_bpp; i != 0; --i)
                {
                    int b = m_output[dst-m_bpp] - m_output[dst];
                    m_output[dst++] = (byte)b;
                }
                for (int j = m_height - 1; j != 0; --j)
                {
                    for (i = 0; i != m_bpp; ++i)
                    {
                        m_output[dst] = (byte)(m_output[dst-stride] - m_output[dst]);
                        ++dst;
                    }
                    for (i = stride-m_bpp; i != 0; --i)
                    {
                        int b = ((int)m_output[dst-stride] + m_output[dst-m_bpp]) >> 1;
                        b -= m_output[dst];
                        m_output[dst++] = (byte)b;
                    }
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
