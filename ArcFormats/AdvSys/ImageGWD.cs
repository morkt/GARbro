//! \file       ImageGWD.cs
//! \date       Thu Oct 13 03:47:44 2016
//! \brief      AdvSys3 engine image format.
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
using GameRes.Utility;

namespace GameRes.Formats.AdvSys
{
    internal class GwdMetaData : ImageMetaData
    {
        public uint DataSize;
    }

    [Export(typeof(ImageFormat))]
    public class GwdFormat : ImageFormat
    {
        public override string         Tag { get { return "GWD"; } }
        public override string Description { get { return "AdvSys3 engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[12];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 4, "GWD"))
                return null;
            return new GwdMetaData
            {
                Width   = BigEndian.ToUInt16 (header, 7),
                Height  = BigEndian.ToUInt16 (header, 9),
                BPP     = header[11],
                DataSize = LittleEndian.ToUInt32 (header, 0),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Gray8;
            byte[] image;
            using (var reader = new GwdReader (stream, info))
            {
                image = reader.Unpack();
            }
            var meta = (GwdMetaData)info;
            stream.Position = 4 + meta.DataSize;
            if (24 == info.BPP && 1 == stream.ReadByte())
            {
                using (var alpha_stream = new StreamRegion (stream, stream.Position, true))
                {
                    var alpha_info = ReadMetaData (alpha_stream) as GwdMetaData;
                    if (null != alpha_info && 8 == alpha_info.BPP
                        && alpha_info.Width == info.Width && alpha_info.Height == info.Height)
                    {
                        alpha_stream.Position = 0;
                        using (var reader = new GwdReader (alpha_stream, alpha_info))
                        {
                            var alpha = reader.Unpack();
                            var pixels = new byte[info.Width * info.Height * 4];
                            int src = 0;
                            int dst = 0;
                            int a = 0;
                            while (dst < pixels.Length)
                            {
                                pixels[dst++] = image[src++];
                                pixels[dst++] = image[src++];
                                pixels[dst++] = image[src++];
                                pixels[dst++] = (byte)~alpha[a++];
                            }
                            image = pixels;
                            format = PixelFormats.Bgra32;
                        }
                    }
                }
            }
            return ImageData.Create (info, format, null, image);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GwdFormat.Write not implemented");
        }
    }

    internal sealed class GwdReader : IDisposable
    {
        MsbBitStream    m_input;
        int             m_width;
        int             m_height;
        int             m_bpp;
        int             m_stride;
        byte[]          m_output;
        byte[]          m_line_buf;

        public byte[] Pixels { get { return m_output; } }
        public int InputSize { get; private set; }

        public GwdReader (Stream input, ImageMetaData info)
        {
            m_bpp = info.BPP;
            if (m_bpp != 8 && m_bpp != 24)
                throw new InvalidFormatException();

            m_input = new MsbBitStream (input, true);
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_stride = m_width * m_bpp / 8;
            m_output = new byte[m_stride * m_height];
            m_line_buf = new byte[m_width];
        }

        public byte[] Unpack ()
        {
            m_input.Input.Position = 12;
            if (8 == m_bpp)
                Read8bpp();
            else
                Read24bpp();
            return m_output;
        }

        void Read8bpp ()
        {
            int dst = 0;
            for (int y = 0; y < m_height; ++y)
            {
                FillLine();
                for (int src = 0; src < m_width; ++src)
                {
                    m_output[dst++] = m_line_buf[src];
                }
            }
        }

        void Read24bpp ()
        {
            int dst = 0;
            for (int y = 0; y < m_height; ++y)
            {
                for (int c = 0; c < 3; ++c)
                {
                    FillLine();
                    int p = dst + c;
                    for (int src = 0; src < m_width; ++src)
                    {
                        m_output[p] = m_line_buf[src];
                        p += 3;
                    }
                }
                dst += m_stride;
            }
        }

        void FillLine ()
        {
            for (int dst = 0; dst < m_width; )
            {
                int length = m_input.GetBits (3);
                if (-1 == length)
                    throw new EndOfStreamException();
                int count = GetCount() + 1;
                if (length != 0)
                {
                    for (int j = 0; j < count; ++j)
                        m_line_buf[dst++] = (byte)m_input.GetBits (length+1);
                }
                else
                {
                    for (int j = 0; j < count; ++j)
                        m_line_buf[dst++] = 0;
                }
            }
            for (int i = 1; i < m_width; ++i)
            {
                m_line_buf[i] = DeltaTable[ m_line_buf[i], m_line_buf[i-1] ];
            }
        }

        int GetCount ()
        {
            int n = 1;
            while (0 == m_input.GetNextBit())
                ++n;
            return m_input.GetBits (n) + (1 << n) - 2;
        }

        static readonly byte[,] DeltaTable = InitTable();

        static byte[,] InitTable ()
        {
            var table = new byte[0x100, 0x100];
            for (int j = 0; j < 0x100; ++j)
            for (int i = 0; i < 0x100; ++i)
            {
                int prev = i;
                if (i >= 0x80)
                    prev = 0xFF - i;
                int v;
                if (2 * prev < j)
                {
                    v = j;
                }
                else if (0 != (j & 1))
                {
                    v = prev + ((j + 1) >> 1);
                }
                else
                {
                    v = prev - (j >> 1);
                }
                if (i >= 0x80)
                    table[j,i] = (byte)(0xFF - v);
                else
                    table[j,i] = (byte)v;
            }
            return table;
        }

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
