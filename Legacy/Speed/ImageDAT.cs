//! \file       ImageDAT.cs
//! \date       2018 Jul 07
//! \brief      Studio Jikkenshitsu compressed image.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [010719][Studio Jikkenshitsu] Shin Gekka Bijin ~Hitori Shizuka

namespace GameRes.Formats.Speed
{
    internal class SpMetaData : ImageMetaData
    {
        public int  Flags;
        public int  Colors;
    }

    [Export(typeof(ImageFormat))]
    public class SpDatFormat : ImageFormat
    {
        public override string         Tag { get { return "DAT/SPEED"; } }
        public override string Description { get { return "Studio Jikkenshitsu image format"; } }
        public override uint     Signature { get { return 0; } }

        public SpDatFormat ()
        {
            Extensions = new string[0];
            Signatures = new uint[] { 0x010003, 0x010007, 0x01000B, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x22);
            if (header.ToInt32 (4) != 0)
                return null;
            int flags = header.ToUInt16 (0);
            if ((flags & ~0xF) != 0)
                return null;
            return new SpMetaData {
                Width = header.ToUInt16 (0x16),
                Height = header.ToUInt16 (0x18),
                BPP = 8,
                Flags = flags,
                Colors = header.ToUInt16 (0x1E),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new SpReader (file, (SpMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpDatFormat.Write not implemented");
        }
    }

    internal class SpReader
    {
        IBinaryStream   m_input;
        SpMetaData      m_info;
        byte[]          m_output;
        int             m_stride;

        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return m_stride; } }
        public BitmapPalette Palette { get; private set; }

        public SpReader (IBinaryStream input, SpMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[info.Width * info.Height];
            m_stride = (int)info.Width;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x22;
            int packed_size = m_input.ReadInt32();
            Palette = ImageFormat.ReadPalette (m_input.AsStream, m_info.Colors);
            UnpackRle (packed_size, m_output);
            if (packed_size != 0 && (m_info.Flags & 4) != 0)
            {
                var alpha = new byte[m_output.Length >> 1];
                packed_size = m_input.ReadInt32();
                UnpackRle (packed_size, alpha);
//                Format = PixelFormats.Gray4;
//                m_stride >>= 1;
//                return alpha;
                return ConvertToRgbA (alpha);
            }
            Format = PixelFormats.Indexed8;
            return m_output;
        }

        byte[] ConvertToRgbA (byte[] alpha)
        {
            m_stride = (int)m_info.Width * 4;
            var pixels = new byte[m_stride * (int)m_info.Height];
            var colors = Palette.Colors;
            int dst = 0;
            for (int src = 0; src < m_output.Length; ++src)
            {
                var color = colors[m_output[src]];
                int a = ((alpha[src >> 1] >> ((~src & 1) << 2)) & 0xF) * 0x11;
                pixels[dst++] = color.B;
                pixels[dst++] = color.G;
                pixels[dst++] = color.R;
                pixels[dst++] = (byte)a;
            }
            Format = PixelFormats.Bgra32;
            return pixels;
        }

        void UnpackRle (int packed_size, byte[] output)
        {
            if (0 == packed_size)
            {
                m_input.Read (output, 0, output.Length);
                return;
            }
            int dst = 0;
            int state = 0;
            byte pixel = 0;
            for (int i = 0; i < packed_size; ++i)
            {
                int rep = m_input.ReadByte();
                if (-1 == rep)
                    break;
                if (0 == state)
                {
                    state = 1;
                    output[dst++] = (byte)rep;
                }
                else if (1 == state)
                {
                    if (output[dst - 1] == rep)
                    {
                        pixel = (byte)rep;
                        state = 2;
                    }
                    output[dst++] = (byte)rep;
                }
                else
                {
                    int count = rep - 2;
                    for (int j = 0; j < count; ++j)
                        output[dst++] = pixel;
                    state = 0;
                }
            }
        }
    }
}
