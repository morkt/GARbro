//! \file       ImageGH.cs
//! \date       2017 Dec 26
//! \brief      Succubus image format.
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

namespace GameRes.Formats.Succubus
{
    internal class GhpMetaData : ImageMetaData
    {
        public int  Colors;
        public uint PaletteOffset;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GhFormat : ImageFormat
    {
        public override string         Tag { get { return "GH"; } }
        public override string Description { get { return "Succubus image format"; } }
        public override uint     Signature { get { return 0x33504847; } } // 'GHP3'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28);
            return new GhpMetaData {
                Width = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP = 8,
                Colors = header.ToUInt16 (0x10),
                PaletteOffset = header.ToUInt32 (0x18),
                DataOffset = header.ToUInt32 (0x24),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Ghp3Reader (file, (GhpMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GhFormat.Write not implemented");
        }
    }

    internal class Ghp3Reader
    {
        IBinaryStream       m_input;
        GhpMetaData         m_info;
        byte[]              m_output;

        public PixelFormat    Format { get { return PixelFormats.Indexed8; } }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get { return m_stride; } }
        public byte[]           Data { get { return m_output; } }

        public Ghp3Reader (IBinaryStream input, GhpMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = ((int)info.Width + 3) & ~3;
        }

        public void Unpack ()
        {
            m_input.Position = m_info.PaletteOffset;
            Palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors, PaletteFormat.Bgr);
            m_input.Position = m_info.DataOffset;
            int image_size = (m_stride * (int)m_info.Height + 0x1F) & ~0x1F;
            int table_size = image_size >> 3;
            var rows_table = new int[m_info.Height];
            for (uint i = 0; i < info.Height; ++i)
            {
                int line_pos = i * m_stride;
                uint y = m_info.Height - i;
                rows_table[y - 1] = line_pos;
            }
        }
    }
}
