//! \file       ImageGD.cs
//! \date       Fri Jan 20 07:07:47 2017
//! \brief      C4 engine image format.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.C4
{
    internal class GdMetaData : ImageMetaData
    {
        public uint DataOffset;
        public int  Compression;
    }

    [Export(typeof(ImageFormat))]
    public class GdFormat : ImageFormat
    {
        public override string         Tag { get { return "GD/C4"; } }
        public override string Description { get { return "C4 engine image format"; } }
        public override uint     Signature { get { return 0x1A324447; } } // 'GD2\x1A'

        public GdFormat ()
        {
            Signatures = new uint[] { 0x1A324447, 0x1A334447 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (4);
            if (!header.AsciiEqual (0, "GD"))
                return null;
            int version = header[2] - '0';
            GdMetaData info;
            if (2 == version)
                info = new GdMetaData { Width = 640, Height = 480, BPP = 24 };
            else if (3 == version)
                info = new GdMetaData { Width = 800, Height = 600, BPP = 24 };
            else
                return null;
            file.Position = 4 + 3 * (info.Width / 10) * (info.Height / 10 - 1);
            int compression = file.ReadByte();
            if (compression != 'b' && compression != 'l' && compression != 'p')
                return null;
            info.Compression = compression;
            info.DataOffset = (uint)file.Position + 1;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GdReader (file, (GdMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GdFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class XexGdFormat : ImageFormat
    {
        public override string         Tag { get { return "GD/XEX"; } }
        public override string Description { get { return "Complets XEX engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".GD"))
                return null;
            var header = file.ReadHeader (2);
            int compression = header[0];
            if (compression != 'l' && compression != 'p' || header[1] != 0x1A)
                return null;
            return new GdMetaData {
                Width = 640, Height = 480, BPP = 24,
                Compression = compression,
                DataOffset = 2,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GdReader (file, (GdMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("XexGdFormat.Write not implemented");
        }
    }

    internal sealed class GdReader
    {
        IBinaryStream   m_input;
        GdMetaData      m_info;
        byte[]          m_output;

        public int Stride { get { return (int)m_info.Width * 3; } }

        public GdReader (IBinaryStream input, GdMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.Width * m_info.Height * 3];
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            if ('b' == m_info.Compression)
            {
                m_input.Read (m_output, 0, m_output.Length);
            }
            else
            {
                using (var bits = new MsbBitStream (m_input.AsStream, true))
                {
                    if ('l' == m_info.Compression)
                        UnpackL (bits);
                    else if ('p' == m_info.Compression)
                        UnpackP (bits);
                    else
                        throw new InvalidFormatException();
                }
            }
            return m_output;
        }

        void UnpackL (IBitStream input)
        {
            int dst = 0;
            var frame = new byte[0x10000];
            int frame_pos = 1;
            while (dst < m_output.Length)
            {
                int bit = input.GetNextBit();
                if (-1 == bit)
                    break;
                if (0 != bit)
                {
                    byte v = (byte)input.GetBits (8);
                    m_output[dst++] = v;
                    frame[frame_pos++ & 0xFFFF] = v;
                }
                else
                {
                    int offset = input.GetBits (16);
                    int count = input.GetBits (4);
                    if (-1 == offset || -1 == count)
                        break;
                    count += 3;
                    while (count --> 0)
                    {
                        byte v = frame[offset++ & 0xFFFF];
                        m_output[dst++] = v;
                        frame[frame_pos++ & 0xFFFF] = v;
                    }
                }
            }
        }

        void UnpackP (IBitStream input)
        {
            int dst = 0;
            for (int i = 0; i < m_output.Length; ++i)
                m_output[i] = 0xFF;
            int width = (int)m_info.Width;
            while (dst < m_output.Length)
            {
                int count = input.GetBits (2);
                if (-1 == count)
                    break;
                if (2 == count)
                {
                    count = input.GetBits (2) + 2;
                }
                else if (3 == count)
                {
                    int n = 3;
                    while (input.GetNextBit() > 0)
                        ++n;
                    if (n >= 24)
                        break;
                    count = (1 << n | input.GetBits (n)) - 2;
                }
                dst += 3 * count;
                m_output [dst  ] = (byte)input.GetBits (8);
                m_output [dst+1] = (byte)input.GetBits (8);
                m_output [dst+2] = (byte)input.GetBits (8);
                if (input.GetNextBit() > 0)
                {
                    int copy_dst = dst;
                    for (;;)
                    {
                        int ctl = input.GetBits (2);
                        if (0 == ctl)
                        {
                            if (input.GetNextBit() <= 0)
                                break;
                            if (input.GetNextBit() > 0)
                                copy_dst += (width + 2) * 3;
                            else
                                copy_dst += (width - 2) * 3;
                        }
                        else if (1 == ctl)
                            copy_dst += (width - 1) * 3;
                        else if (2 == ctl)
                            copy_dst += width * 3;
                        else if (3 == ctl)
                            copy_dst += (width + 1) * 3;
                        else if (-1 == ctl)
                            break;
                        m_output[copy_dst]   = m_output[dst];
                        m_output[copy_dst+1] = m_output[dst+1];
                        m_output[copy_dst+2] = m_output[dst+2];
                    }
                }
                dst += 3;
            }
            byte b = 0, g = 0, r = 0;
            for (dst = 0; dst < m_output.Length; dst += 3)
            {
                if (0xFF == m_output[dst] && 0xFF == m_output[dst+1] && 0xFF == m_output[dst+2])
                {
                    m_output[dst  ] = b;
                    m_output[dst+1] = g;
                    m_output[dst+2] = r;
                }
                else
                {
                    b = m_output[dst  ];
                    g = m_output[dst+1];
                    r = m_output[dst+2];
                }
            }
        }
    }
}
