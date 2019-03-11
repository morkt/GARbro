//! \file       ImageGRC.cs
//! \date       2019 Mar 09
//! \brief      Sudio
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Jikkenshitsu
{
    internal class GrcMetaData : ImageMetaData
    {
        public int  BitsOffset;
        public int  BitsLength;
        public int  DataOffset;
        public int  DataLength;
        public int  AlphaOffset;
        public int  AlphaLength;
    }

    [Export(typeof(ImageFormat))]
    public class GrcFormat : ImageFormat
    {
        public override string         Tag { get { return "GRC"; } }
        public override string Description { get { return "Studio Jikkenshitsu image format"; } }
        public override uint     Signature { get { return 0x08; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".grc"))
                return null;
            var header = file.ReadHeader (0x20);
            int bpp = header.ToInt32 (0);
            if (bpp != 8)
                return null;
            return new GrcMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = bpp,
                BitsOffset = header.ToInt32 (8),
                BitsLength = header.ToInt32 (12),
                DataOffset = header.ToInt32 (16),
                DataLength = header.ToInt32 (20),
                AlphaOffset = header.ToInt32 (24),
                AlphaLength = header.ToInt32 (28),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrcReader (file, (GrcMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrcFormat.Write not implemented");
        }
    }

    internal class GrcReader
    {
        IBinaryStream       m_input;
        GrcMetaData         m_info;
        int                 m_stride;
        byte[]              m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }

        public GrcReader (IBinaryStream input, GrcMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = m_info.iWidth * m_info.BPP / 8;
            m_output = new byte[m_stride * m_info.iHeight];
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x20;
            Format = PixelFormats.Indexed8;

            if (8 == m_info.BPP)
                Palette = ImageFormat.ReadPalette (m_input.AsStream);

            var rowsCtl = m_input.ReadBytes (m_info.iHeight);
            m_input.Position = m_info.BitsOffset;
            var ctlBits = m_input.ReadBytes (m_info.BitsLength);
            m_input.Position = m_info.DataOffset;

            int src1 = 0;
            int dst = 0;
            var coord = new int[4,4] {
                { 0, -1, -m_stride, -m_stride - 1 },
                { 0, -1, -2, -3 },
                { 0, -m_stride, -2 * m_stride, -3 * m_stride },
                { 0, -m_stride - 1, -m_stride, -m_stride + 1 }
            };
            int blocks = m_info.iWidth / 4;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int ctl = rowsCtl[y];
                if (ctl > 3)
                    throw new InvalidFormatException();
                for (int x = 0; x < blocks; ++x)
                {
                    int bits = ctlBits[src1++];
                    for (int i = 6; i >= 0; i -= 2)
                    {
                        int p = (bits >> i) & 3;
                        byte px;
                        if (p != 0)
                            px = m_output[dst + coord[ctl,p]];
                        else
                            px = m_input.ReadUInt8();
                        m_output[dst++] = px;
                    }
                }
            }
            return ImageData.CreateFlipped (m_info, Format, Palette, m_output, m_stride);
        }
    }
}
