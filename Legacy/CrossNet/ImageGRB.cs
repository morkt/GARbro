//! \file       ImageGRB.cs
//! \date       2017 Dec 22
//! \brief      CrossNet/Studio Jikkenshitsu image format.
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.CrossNet
{
    internal class GrbMetaData : ImageMetaData
    {
        public uint BitsOffset;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GrbFormat : ImageFormat
    {
        public override string         Tag { get { return "GRB"; } }
        public override string Description { get { return "CrossNet image format"; } }
        public override uint     Signature { get { return 8; } }

        public GrbFormat ()
        {
            Signatures = new uint[] { 8, 1 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToInt32 (0);
            uint width = header.ToUInt16 (4);
            uint height = header.ToUInt16 (6);
            uint bits_offset = header.ToUInt32 (8);
            uint data_offset = header.ToUInt32 (0x10);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000
                || bits_offset >= file.Length || bits_offset < 0x18
                || data_offset >= file.Length || data_offset < 0x18)
                return null;
            return new GrbMetaData {
                Width = width,
                Height = height,
                BPP = bpp,
                BitsOffset = bits_offset,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrbReader (file, (GrbMetaData)info);
            reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrbFormat.Write not implemented");
        }
    }

    internal sealed class GrbReader
    {
        IBinaryStream   m_input;
        GrbMetaData     m_info;
        int             m_width;
        int             m_stride;
        int             m_height;
        byte[]          m_output;

        public int            Stride { get { return m_stride; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]           Data { get { return m_output; } }

        public GrbReader (IBinaryStream input, GrbMetaData info)
        {
            m_input = input;
            m_info = info;
            m_height = (int)info.Height;
            switch (info.BPP)
            {
            case 1:
                Format = PixelFormats.Indexed1;
                m_width = (int)(info.Width + 0x1F) & ~0x1F;
                m_stride = m_width / 8;
                break;

            case 8:
                Format = PixelFormats.Indexed8;
                m_width = (int)(info.Width + 3) & ~3;
                m_stride = m_width;
                break;

            default:
                throw new InvalidFormatException();
            }
            m_output = new byte[m_height * m_stride];
        }

        public void Unpack ()
        {
            m_input.Position = 0x18;
            if (m_info.BPP < 15)
            {
                Palette = ImageFormat.ReadPalette (m_input.AsStream, 1 << m_info.BPP);
            }
            var row_data = m_input.ReadBytes (m_height);

            int blocks = m_width >> 2;
            m_input.Position = m_info.BitsOffset;
            var ctl_data = m_input.ReadBytes (m_height * blocks);

            int src = 0;
            int dst = 0;
            var offsets = new int[4,4] {
                { 0, -1, -m_width, -m_width-1 },
                { 0, -1, -2, -3 },
                { 0, -m_width, -m_width * 2, -m_width * 3 },
                { 0, -m_width-1, -m_width, -m_width+1 }
            };
            m_input.Position = m_info.DataOffset;

            for (int y = 0; y < m_height; ++y)
            {
                byte row = row_data[y];
                for (int x = 0; x < blocks; ++x)
                {
                    byte ctl = ctl_data[src+x];
                    for (int bit_pos = 6; bit_pos >= 0; bit_pos -= 2)
                    {
                        byte v;
                        int bits = (ctl >> bit_pos) & 3;
                        if (bits != 0)
                            v = m_output[dst + offsets[row,bits]];
                        else
                            v = m_input.ReadUInt8();
                        m_output[dst++] = v;
                    }
                }
                src += blocks;
            }
        }
    }
}
