//! \file       ImageRC8.cs
//! \date       Thu Jan 05 00:53:42 2017
//! \brief      RC8 image format implementation.
//
// Copyright (C) 2014-2017 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Majiro
{
    [Export(typeof(ImageFormat))]
    public class Rc8Format : ImageFormat
    {
        public override string         Tag { get { return "RC8"; } }
        public override string Description { get { return "Majiro game engine indexed image format"; } }
        public override uint     Signature { get { return 0x9A925A98; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (8);
            if (!header.AsciiEqual (4, "8_00"))
                return null;
            uint width = stream.ReadUInt32();
            uint height = stream.ReadUInt32();
            if (width > 0x8000 || height > 0x8000)
                return null;
            return new ImageMetaData
            {
                Width   = width,
                Height  = height,
                OffsetX = 0,
                OffsetY = 0,
                BPP     = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new Reader (file, info))
            {
                reader.Unpack();
                var palette = new BitmapPalette (reader.Palette);
                return ImageData.Create (info, PixelFormats.Indexed8, palette, reader.Data, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Rc8Format.Write is not implemented.");
        }

        internal sealed class Reader : IDisposable
        {
            private IBinaryStream   m_input;
            private uint            m_width;
            private Color[]         m_palette;
            private byte[]          m_data;

            public Color[] Palette { get { return m_palette; } }
            public byte[]     Data { get { return m_data; } }

            public Reader (IBinaryStream file, ImageMetaData info)
            {
                m_width = info.Width;
                file.Position = 0x14;
                var palette_data = new byte[0x300];
                if (palette_data.Length != file.Read (palette_data, 0, palette_data.Length))
                    throw new InvalidFormatException();
                m_palette = new Color[0x100];
                for (int i = 0; i < 0x100; ++i)
                {
                    m_palette[i] = Color.FromRgb (palette_data[i*3], palette_data[i*3+1], palette_data[i*3+2]);
                }
                m_data = new byte[m_width * info.Height];
                m_input = file;
            }

            private static readonly sbyte[] ShiftTable = new sbyte[] {
                -16, -32, -48, -64,
                49, 33, 17, 1, -15, -31, -47,
                34, 18, 2, -14, -30,
            };

            public void Unpack ()
            {
                int data_pos = 0;
                int eax = 0;
                int pixels_remaining = m_data.Length;
                while (pixels_remaining > 0)
                {
                    int count = eax + 1;
                    if (count > pixels_remaining)
                        throw new InvalidFormatException();
                    pixels_remaining -= count;

                    if (count != m_input.Read (m_data, data_pos, count))
                        throw new InvalidFormatException();
                    data_pos += count;

                    while (pixels_remaining > 0)
                    {
                        eax = m_input.ReadByte();
                        if (0 == (eax & 0x80))
                        {
                            if (0x7f == eax)
                                eax += m_input.ReadUInt16();
                            break;
                        }
                        int shift_index = eax >> 3;
                        eax &= 7;
                        if (7 == eax)
                            eax += m_input.ReadUInt16();

                        count = eax + 3;
                        if (pixels_remaining < count)
                            throw new InvalidFormatException();
                        pixels_remaining -= count;
                        int shift = ShiftTable[shift_index & 0x0f];
                        int shift_row = shift & 0x0f;
                        shift >>= 4;
                        shift_row *= (int)m_width;
                        shift -= shift_row;
                        if (shift >= 0 || data_pos+shift < 0)
                            throw new InvalidFormatException();
                        Binary.CopyOverlapped (m_data, data_pos+shift, data_pos, count);
                        data_pos += count;
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
}
