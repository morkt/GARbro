//! \file       ImagePRT.cs
//! \date       2018 Nov 18
//! \brief      KID image format.
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Kid
{
    internal class PrtMetaData : ImageMetaData
    {
        public int      Version;
        public ushort   PaletteOffset;
        public ushort   DataOffset;
        public bool     HasAlpha;
    }

    [Export(typeof(ImageFormat))]
    public class PrtFormat : ImageFormat
    {
        public override string         Tag { get { return "PRT"; } }
        public override string Description { get { return "KID image format"; } }
        public override uint     Signature { get { return 0x545250; } } // 'PRT'

        public PrtFormat ()
        {
            Extensions = new[] { "prt", "cps" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            int version = header.ToUInt16 (4);
            if (version != 101 && version != 102)
                return null;
            var info = new PrtMetaData {
                Width  = header.ToUInt16 (0xC),
                Height = header.ToUInt16 (0xE),
                BPP    = header.ToUInt16 (6),
                Version = version,
                PaletteOffset = header.ToUInt16 (8),
                DataOffset = header.ToUInt16 (0xA),
                HasAlpha = header.ToInt32 (0x10) != 0,
            };
            if (102 == version)
            {
                info.OffsetX = file.ReadInt32();
                info.OffsetY = file.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PrtReader (file, (PrtMetaData)info);
            return reader.GetImage();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PrtFormat.Write not implemented");
        }
    }

    internal class PrtReader
    {
        IBinaryStream   m_input;
        PrtMetaData     m_info;
        int             m_stride;

        PixelFormat    Format { get; set; }
        BitmapPalette Palette { get; set; }

        public PrtReader (IBinaryStream input, PrtMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = ((int)info.Width * (info.BPP / 8) + 3) & ~3;
        }

        public ImageData GetImage ()
        {
            if (8 == m_info.BPP)
            {
                m_input.Position = m_info.PaletteOffset;
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
                Format = PixelFormats.Indexed8;
            }
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (32 == m_info.BPP)
                Format = PixelFormats.Bgr32;
            else
                throw new InvalidFormatException();

            m_input.Position = m_info.DataOffset;
            var pixels = m_input.ReadBytes (m_stride * (int)m_info.Height);
            if (!m_info.HasAlpha)
                return ImageData.CreateFlipped (m_info, Format, Palette, pixels, m_stride);

            var alpha = m_input.ReadBytes ((int)m_info.Width * (int)m_info.Height);
            if (8 == m_info.BPP)
                pixels = ApplyAlphaIndexed (pixels, alpha);
            else
                pixels = ApplyAlphaRgb (pixels, alpha);
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels);
        }

        byte[] ApplyAlphaIndexed (byte[] input, byte[] alpha)
        {
            int width = (int)m_info.Width;
            int dst_stride = width * 4;
            var output = new byte[dst_stride * (int)m_info.Height];
            int dst_row = 0;
            int scr_row = input.Length - m_stride;
            int asrc = 0;
            var colors = Palette.Colors;
            while (dst_row < output.Length)
            {
                int dst = dst_row;
                for (int x = 0; x < width; ++x)
                {
                    var color = colors[input[scr_row+x]];
                    output[dst++] = color.B;
                    output[dst++] = color.G;
                    output[dst++] = color.R;
                    output[dst++] = alpha[asrc++];
                }
                dst_row += dst_stride;
                scr_row -= m_stride;
            }
            return output;
        }

        byte[] ApplyAlphaRgb (byte[] input, byte[] alpha)
        {
            int src_pixel_size = m_info.BPP / 8;
            int width = (int)m_info.Width;
            int dst_stride = width * 4;
            var output = new byte[dst_stride * (int)m_info.Height];
            int dst_row = 0;
            int src_row = input.Length - m_stride;
            int asrc = 0;
            while (dst_row < output.Length)
            {
                int dst = dst_row;
                int src = src_row;
                for (int x = 0; x < width; ++x)
                {
                    output[dst++] = input[src  ];
                    output[dst++] = input[src+1];
                    output[dst++] = input[src+2];
                    output[dst++] = alpha[asrc++];
                    src += src_pixel_size;
                }
                dst_row += dst_stride;
                src_row -= m_stride;
            }
            return output;
        }
    }
}
