//! \file       ImageZIT.cs
//! \date       Mon Apr 04 18:59:57 2016
//! \brief      Silky's image format.
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

namespace GameRes.Formats.Silky
{
    internal class ZitMetaData : ImageMetaData
    {
        public ushort   Type;
        public int      Colors;
    }

    [Export(typeof(ImageFormat))]
    public class ZitFormat : ImageFormat
    {
        public override string         Tag { get { return "ZIT"; } }
        public override string Description { get { return "Silky's image format"; } }
        public override uint     Signature { get { return 0x1803545A; } } // 'ZT'

        public ZitFormat ()
        {
            Signatures = new uint[] { 0x1803545A, 0x2084545A, 0x8803545A };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            return new ZitMetaData
            {
                Type = header.ToUInt16 (2),
                Width = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                BPP = 32,
                Colors = header.ToUInt16 (4),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new ZitReader (stream, (ZitMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, PixelFormats.Bgra32, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ZitFormat.Write not implemented");
        }
    }

    internal class ZitReader : IDisposable
    {
        IBinaryStream   m_input;
        byte[]          m_output;
        int             m_width;
        int             m_height;
        ushort          m_type;
        int             m_colors;

        public byte[] Data { get { return m_output; } }

        public ZitReader (IBinaryStream input, ZitMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_type = info.Type;
            m_colors = info.Colors;
            m_output = new byte[m_width*m_height*4];
        }

        public void Unpack ()
        {
            m_input.Position = 0x10;
            switch (m_type)
            {
            case 0x1803: Unpack1(); break;
            case 0x2084: Unpack2(); break;
            case 0x8803: Unpack3(); break;
            default:
                throw new InvalidFormatException();
            }
        }

        void Unpack1 ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                byte b = m_input.ReadUInt8();
                byte g = m_input.ReadUInt8();
                byte r = m_input.ReadUInt8();
                if (b != 0 || g != 0xFF || r != 0)
                {
                    m_output[dst++] = b;
                    m_output[dst++] = g;
                    m_output[dst++] = r;
                    m_output[dst++] = 0xFF;
                }
                else
                {
                    LittleEndian.Pack (0xFFFFu, m_output, dst);
                    dst += 4;
                }
            }
        }

        void Unpack2 ()
        {
            m_input.Read (m_output, 0, m_output.Length);
        }

        void Unpack3 ()
        {
            var palette = m_input.ReadBytes (m_colors * 3);
            int dst = 0;
            while (dst < m_output.Length)
            {
                int index = m_input.ReadByte() * 3;
                byte b = palette[index];
                byte g = palette[index+1];
                byte r = palette[index+2];
                if (b != 0 || g != 0xFF || r != 0)
                {
                    m_output[dst++] = b;
                    m_output[dst++] = g;
                    m_output[dst++] = r;
                    m_output[dst++] = 0xFF;
                }
                else
                {
                    LittleEndian.Pack (0xFF00u, m_output, dst);
                    dst += 4;
                }
            }
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
