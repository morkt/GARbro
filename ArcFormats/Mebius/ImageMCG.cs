//! \file       ImageMCG.cs
//! \date       2018 Oct 13
//! \brief      Mebius engine image format.
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

// [041224][Studio Ring] Oyatsu no Jikan

namespace GameRes.Formats.Mebius
{
    internal class McgMetaData : ImageMetaData
    {
        public byte Method;
    }

    [Export(typeof(ImageFormat))]
    public class McgFormat : ImageFormat
    {
        public override string         Tag { get { return "MCG/MEBIUS"; } }
        public override string Description { get { return "Mebius image format"; } }
        public override uint     Signature { get { return 0x0247434D; } } // 'MCG'

        public McgFormat ()
        {
            Extensions = new string[] { "mcg", "msk" };
            Signatures = new uint[] { 0x0247434D, 0x0347434D, 0x0447434D, 0x0547434D, 0x0047434D, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            byte type = header[3];
            if (!header.AsciiEqual (0, "MCG") || type > 7)
                return null;
            return new McgMetaData {
                Width  = BigEndian.ToUInt16 (header, 12),
                Height = BigEndian.ToUInt16 (header, 14),
                OffsetX = BigEndian.ToInt16 (header, 8),
                OffsetY = BigEndian.ToInt16 (header, 10),
                BPP = 5 == type || 4 == type ? 8 : 32,
                Method = type,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new McgReader (file, (McgMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("McgFormat.Write not implemented");
        }
    }

    internal class McgReader
    {
        IBinaryStream   m_input;
        McgMetaData     m_info;
        byte[]          m_output;
        int             m_stride;

        public McgReader (IBinaryStream input, McgMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = (int)m_info.Width * (m_info.BPP / 8);
            m_output = new byte[m_stride * (int)m_info.Height];
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x10;
            switch (m_info.Method)
            {
            case 0: UnpackV0();  break;
            case 4:
            case 1: UnpackV1();  break;
            case 2: UnpackRgbRle (3);  break;
            case 3: UnpackRgbRle (4);  break;
            case 5: UnpackV5(); break;
            case 6:
            case 7: break;
            default: throw new InvalidFormatException();
            }
            PixelFormat format = 8 == m_info.BPP ? PixelFormats.Gray8
                               : 3 == m_info.Method ? PixelFormats.Bgra32
                               : PixelFormats.Bgr32;
            return ImageData.CreateFlipped (m_info, format, null, m_output, m_stride);
        }

        void UnpackV0 ()
        {
            for (int c = 0; c < 3; c++)
            for (int dst = c; dst < m_output.Length; dst += 4)
            {
                m_output[dst] = m_input.ReadUInt8();
            }
        }

        void UnpackV1 ()
        {
            m_input.Read (m_output, 0, m_output.Length);
        }

        void UnpackRgbRle (int channels)
        {
            byte rle_code = m_input.ReadUInt8();
            for (int c = 0; c < channels; c++)
            for (int dst = c; dst < m_output.Length; )
            {
                byte code = m_input.ReadUInt8();
                if (code == rle_code)
                {
                    int count = m_input.ReadUInt8();
                    byte v = m_input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst] = v;
                        dst += 4;
                    }
                }
                else
                {
                    m_output[dst] = code;
                    dst += 4;
                }
            }
        }

        void UnpackV5 ()
        {
            byte rle_code = m_input.ReadUInt8();
            int dst = 0;
            while (dst < m_output.Length)
            {
                byte code = m_input.ReadUInt8();
                if (code == rle_code)
                {
                    int count = m_input.ReadUInt8();
                    byte v = m_input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst++] = v;
                    }
                }
                else
                {
                    m_output[dst++] = code;
                }
            }
        }
    }
}
