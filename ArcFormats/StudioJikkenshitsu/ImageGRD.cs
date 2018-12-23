//! \file       ImageGRD.cs
//! \date       2018 Jul 27
//! \brief      Studio Jikkenshitsu image format.
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
using GameRes.Compression;
using GameRes.Utility;

// [030411][Studio Jikkenshitsu] Giin Oyako

namespace GameRes.Formats.Jikkenshitsu
{
    internal class GrdMetaData : ImageMetaData
    {
        public int      PackedLength;
        public int      AlphaLength;
        public bool     IsEncrypted;
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class GrdFormat : ImageFormat
    {
        public override string         Tag { get { return "GRD/SJ"; } }
        public override string Description { get { return "Studio Jikkenshitsu image format"; } }
        public override uint     Signature { get { return 0x20445247; } } // 'GRD '

        // Giin Oyako
        static readonly byte[] DefaultKey = { 15, 0, 1, 2, 8, 5, 10, 11, 5, 9, 14, 13, 1, 8, 0, 6 };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            return new GrdMetaData {
                Width  = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                BPP    = header[4],
                PackedLength = header.ToInt32 (0xC),
                AlphaLength  = header.ToInt32 (0x14),
                IsEncrypted  = (header[5] & 0x80) != 0,
                Key    = DefaultKey,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new GrdReader (file, (GrdMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrdFormat.Write not implemented");
        }
    }

    internal class GrdReader
    {
        IBinaryStream   m_input;
        GrdMetaData     m_info;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get; private set; }

        public GrdReader (IBinaryStream input, GrdMetaData info)
        {
            m_input = input;
            m_info = info;
            if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
            int width = (int)m_info.Width;
            if (m_info.BPP <= 8)
                width = (width + 3) & ~3;
            Stride = width * m_info.BPP / 8;
            m_output = new byte[Stride * (int)m_info.Height];
        }

        public ImageData Unpack ()
        {
            Stream input = new StreamRegion (m_input.AsStream, 0x18, m_info.PackedLength, true);
            if (m_info.IsEncrypted)
                input = new InputCryptoStream (input, new SjTransform (m_info.Key));
            using (input = new LzssStream (input))
            {
                var header = new byte[0x28];
                input.Read (header, 0, header.Length);
                if (8 == m_info.BPP)
                    Palette = ImageFormat.ReadPalette (input);
                input.Read (m_output, 0, m_output.Length);
            }
            if (m_info.AlphaLength > 0 && m_info.BPP == 8)
            {
                m_input.Position = 0x18 + m_info.PackedLength;
                var alpha = new byte[m_info.AlphaLength];
                using (var lzss = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                    lzss.Read (alpha, 0, alpha.Length);
                return ApplyAlpha (alpha);
            }
            return ImageData.CreateFlipped (m_info, Format, Palette, m_output, Stride);
        }

        ImageData ApplyAlpha (byte[] alpha)
        {
            int width = (int)m_info.Width;
            int height = (int)m_info.Height;
            int dst_stride = width * 4;
            var pixels = new byte[dst_stride * height];
            var colors = Palette.Colors;

            var row_table = new ushort[height];
            int row_table_size = row_table.Length * sizeof(ushort);
            Buffer.BlockCopy (alpha, 0, row_table, 0, row_table_size);
            var lines = new int[height];
            int lines_size = lines.Length * sizeof(int);
            Buffer.BlockCopy (alpha, row_table_size, lines, 0, lines_size);

            int alpha_pos = row_table_size + lines_size;
            int src = m_output.Length - Stride;
            int dst_row = 0;
            for (int y = 0; y < height; ++y)
            {
                int dst = dst_row;
                for (int x = 0; x < width; ++x)
                {
                    byte code = m_output[src+x];
                    var color = colors[code];
                    pixels[dst++] = color.B;
                    pixels[dst++] = color.G;
                    pixels[dst++] = color.R;
                    pixels[dst++] = (byte)(code != 0 ? 0xFF : 0);
                }
                int asrc = alpha_pos + 4 * lines[y];
                for (int i = 0; i < row_table[y]; ++i)
                {
                    byte a = Math.Min (alpha[asrc+3], (byte)0xF);
                    dst = dst_row + LittleEndian.ToUInt16 (alpha, asrc) * 4;
                    if (a > 0)
                    {
                        var color = colors[alpha[asrc+2]];
                        pixels[dst  ] = color.B;
                        pixels[dst+1] = color.G;
                        pixels[dst+2] = color.R;
                        pixels[dst+3] = (byte)(a * 0x11);
                    }
                    else
                        pixels[dst+3] = 0;
                    asrc += 4;
                }
                dst_row += dst_stride;
                src -= Stride;
            }
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }
    }
}
