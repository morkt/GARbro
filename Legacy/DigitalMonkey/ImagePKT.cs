//! \file       ImagePKT.cs
//! \date       2018 Oct 07
//! \brief      Digital Monkey image format.
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

// [030725][Digital Monkey] Kono Sora ga Tsuieru Toki ni

namespace GameRes.Formats.DigitalMonkey
{
    internal class PktMetaData : ImageMetaData
    {
        public int  Version;
        public uint DataOffset;
        public uint AlphaOffset;
        public bool HasAlpha;
        public int  AlphaRleCount;
    }

    [Export(typeof(ImageFormat))]
    public class PktFormat : ImageFormat
    {
        public override string         Tag { get { return "PKT/DM"; } }
        public override string Description { get { return "Digital Monkey image format"; } }
        public override uint     Signature { get { return 0x31544B50; } } // 'PKT10'

        public PktFormat ()
        {
            Extensions = new [] { "pkt", "msk", /*"dm"*/ };
            Signatures = new uint[] { 0x31544B50, 0x32544B50, 0x39544B50 }; // 'PKT10', 'PKT20', 'PKT99'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            if (header[5] != 0)
                return null;
            int version = header[3] * 10 + header[4] - 528;
            if (version != 10 && version != 20 && version != 99)
                return null;
            return new PktMetaData {
                Width  = header.ToUInt32 (0x14),
                Height = header.ToUInt32 (0x18),
                BPP = 20 == version ? 24 : 8,
                Version = version,
                DataOffset = header.ToUInt32 (0x20),
                AlphaOffset = header.ToUInt32 (0x24),
                HasAlpha = header.ToInt32 (0x28) != 0,
                AlphaRleCount = header.ToInt32 (0x2C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PktReader (file, (PktMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PktFormat.Write not implemented");
        }
    }

    internal class PktReader
    {
        IBinaryStream   m_input;
        PktMetaData     m_info;
        byte[]          m_output;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public PktReader (IBinaryStream input, PktMetaData info)
        {
            m_input = input;
            m_info = info;
            if (99 == m_info.Version)
                Format = PixelFormats.Gray8;
            else if (m_info.HasAlpha)
                Format = PixelFormats.Bgra32;
            else if (10 == m_info.Version)
                Format = PixelFormats.Indexed8;
            else if (20 == m_info.Version)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
            m_output = new byte[(int)m_info.Width * (int)m_info.Height * (m_info.BPP / 8)];
        }

        public byte[] Unpack ()
        {
            if (99 == m_info.Version)
                return ReadGrayScale();
            m_input.Position = m_info.DataOffset;
            if (10 == m_info.Version)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Bgr);
            m_input.Read (m_output, 0, m_output.Length);
            if (!m_info.HasAlpha)
                return m_output;
            m_input.Position = m_info.AlphaOffset;
            int plane_size = (int)m_info.Width * (int)m_info.Height;
            var alpha = new byte[plane_size];
            RleUnpack (m_input, m_info.AlphaRleCount, alpha);
            var pixels = new byte[4 * plane_size];
            if (8 == m_info.BPP)
                ApplyAlpha8bpp (alpha, pixels);
            else
                ApplyAlpha24bpp (alpha, pixels);
            return pixels;
        }

        byte[] ReadGrayScale ()
        {
            m_input.Position = m_info.AlphaOffset + 8;
            RleUnpack (m_input, m_info.AlphaRleCount, m_output);
            return m_output;
        }

        void ApplyAlpha8bpp (byte[] alpha, byte[] output)
        {
            var colors = Palette.Colors;
            int dst = 0;
            for (int src = 0; src < m_output.Length; ++src)
            {
                var color = colors[m_output[src]];
                output[dst++] = color.B;
                output[dst++] = color.G;
                output[dst++] = color.R;
                output[dst++] = alpha[src];
            }
        }

        void ApplyAlpha24bpp (byte[] alpha, byte[] output)
        {
            int src = 0, dst = 0;
            for (int asrc = 0; asrc < alpha.Length; ++asrc)
            {
                output[dst++] = m_output[src++];
                output[dst++] = m_output[src++];
                output[dst++] = m_output[src++];
                output[dst++] = alpha[asrc];
            }
        }

        internal static void RleUnpack (IBinaryStream input, int chunk_count, byte[] output)
        {
            int dst = 0;
            for (int i = 0; i < chunk_count; ++i)
            {
                int count = input.ReadUInt8();
                byte val = input.ReadUInt8();
                for (int j = 0; j < count; ++j)
                    output[dst++] = val;
            }
        }
    }
}
