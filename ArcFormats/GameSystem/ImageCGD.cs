//! \file       ImageCGD.cs
//! \date       Mon Jan 16 05:22:43 2017
//! \brief      'Game System' CG image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ImageFormat))]
    public class CgdFormat : ImageFormat
    {
        public override string         Tag { get { return "CGD"; } }
        public override string Description { get { return "'GameSystem' CG image format"; } }
        public override uint     Signature { get { return 0; } }

        public CgdFormat ()
        {
            Extensions = new string[] { "cgd", "crgb" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature != file.Length)
                return null;
            var header = file.ReadHeader (0x10);
            uint width = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            var reader = new CgdReader (file, info);
            return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CgdFormat.Write not implemented");
        }
    }

    internal sealed class CgdReader : BinaryImageDecoder
    {
        byte[]          m_output;

        public int         Stride { get; private set; }
        public PixelFormat Format { get { return PixelFormats.Bgr24; } }

        public CgdReader (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
            Stride = 3 * (int)info.Width;
            m_output = new byte[Stride * (int)info.Height];
        }

        protected override ImageData GetImageData ()
        {
            var pixels = Unpack();
            return ImageData.CreateFlipped (Info, Format, null, pixels, Stride);
        }

        byte[] Unpack ()
        {
            int dst = 0;
            byte r = 0, g = 0, b = 0;
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadUInt8();
                if (ctl < 0x80)
                {
                    int src = m_input.ReadUInt8() | ctl << 8;
                    b += ColorTable[src,0];
                    g += ColorTable[src,1];
                    r += ColorTable[src,2];
                    m_output[dst++] = b;
                    m_output[dst++] = g;
                    m_output[dst++] = r;
                }
                else if (ctl < 0xC0)
                {
                    int count = ctl - 0x7F;
                    while (count --> 0)
                    {
                        m_output[dst++] = b;
                        m_output[dst++] = g;
                        m_output[dst++] = r;
                    }
                }
                else if (0xFF == ctl)
                {
                    break;
                }
                else
                {
                    int count = (ctl - 0xBF) * 3;
                    m_input.Read (m_output, dst, count);
                    dst += count;
                    b = m_output[dst-3];
                    g = m_output[dst-2];
                    r = m_output[dst-1];
                }
            }
            return m_output;
        }

        internal static readonly byte[,] ColorTable = InitColorTable();

        private static byte[,] InitColorTable ()
        {
            var table = new byte[0x8000,3];
            for (int i = 0; i < 0x8000; ++i)
            {
                int r = (i >> 10) & 0x1F;
                int g = (i >> 5) & 0x1F;
                int b = i & 0x1F;
                if (r > 15)
                    r -= 32;
                if (g > 15)
                    g -= 32;
                if ( b > 15 )
                    b -= 32;
                table[i,0] = (byte)b;
                table[i,1] = (byte)g;
                table[i,2] = (byte)r;
            }
            return table;
        }
    }
}
