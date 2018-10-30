//! \file       ImagePSD.cs
//! \date       Tue Nov 17 18:22:36 2015
//! \brief      Adobe Photoshop file format implementation.
//
// Copyright (C) 2015 by morkt
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Adobe
{
    internal class PsdMetaData : ImageMetaData
    {
        public int  Channels;
        public int  Mode;
    }

    [Export(typeof(ImageFormat))]
    public class PsdFormat : ImageFormat
    {
        public override string         Tag { get { return "PSD"; } }
        public override string Description { get { return "Adobe Photoshop image format"; } }
        public override uint     Signature { get { return 0x53504238; } } // '8BPS'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (26).ToArray();
            int version = BigEndian.ToInt16 (header, 4);
            if (1 != version)
                return null;
            int channels = BigEndian.ToInt16 (header, 0x0C);
            if (channels < 1 || channels > 56)
                return null;
            uint height = BigEndian.ToUInt32 (header, 0x0E);
            uint width  = BigEndian.ToUInt32 (header, 0x12);
            if (width < 1 || width > 30000 || height < 1 || height > 30000)
                return null;
            int bpc = BigEndian.ToInt16 (header, 0x16);
            return new PsdMetaData
            {
                Width = width,
                Height = height,
                Channels = channels,
                BPP = channels * bpc,
                Mode = BigEndian.ToInt16 (header, 0x18),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (PsdMetaData)info;
            using (var reader = new PsdReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PsdFormat.Write not implemented");
        }
    }

    internal class PsdReader : IDisposable
    {
        IBinaryStream   m_input;
        PsdMetaData     m_info;
        byte[]          m_output;
        int             m_channel_size;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]           Data { get { return m_output; } }

        public PsdReader (IBinaryStream input, PsdMetaData info)
        {
            m_info = info;
            m_input = input;
            int bpc = m_info.BPP / m_info.Channels;
            switch (m_info.Mode)
            {
            case 3:
                if (8 != bpc)
                    throw new NotSupportedException();
                if (3 == m_info.Channels)
                    Format = PixelFormats.Bgr24;
                else if (4 == m_info.Channels)
                    Format = PixelFormats.Bgra32;
                else if (m_info.Channels > 4)
                    Format = PixelFormats.Bgr32;
                else
                    throw new NotSupportedException();
                break;
//            case 2: Format = PixelFormats.Indexed8; break;
            case 1:
                if (8 == bpc)
                    Format = PixelFormats.Gray8;
                else if (16 == bpc)
                    Format = PixelFormats.Gray16;
                else
                    throw new NotSupportedException();
                break;
            case 0: Format = PixelFormats.BlackWhite; break;
            default:
                throw new NotImplementedException ("Not supported PSD color mode");
            }
            m_channel_size = (int)m_info.Height * (int)m_info.Width * bpc / 8;
        }

        public void Unpack ()
        {
            m_input.Position = 0x1A;
            int color_data_length = Binary.BigEndian (m_input.ReadInt32());
            long next_pos = m_input.Position + color_data_length;
            if (0 != color_data_length)
            {
                if (8 == m_info.BPP)
                    ReadPalette (color_data_length);
                m_input.Position = next_pos;
            }
            next_pos += 4 + Binary.BigEndian (m_input.ReadInt32());
            m_input.Position = next_pos; // skip Image Resources
            next_pos += 4 + Binary.BigEndian (m_input.ReadInt32());
            m_input.Position = next_pos; // skip Layer and Mask Information

            int compression = Binary.BigEndian (m_input.ReadInt16());
            int remaining = checked((int)(m_input.Length - m_input.Position));
            byte[] pixels;
            if (0 == compression)
                pixels = m_input.ReadBytes (remaining);
            else if (1 == compression)
                pixels = UnpackRLE();
            else
                throw new NotSupportedException ("PSD files with ZIP compression not supported");

            if (1 == m_info.Channels)
            {
                m_output = pixels;
                return;
            }
            int channels = Math.Min (4, m_info.Channels);
            m_output = new byte[channels * m_channel_size];
            int src = 0;
            for (int ch = 0; ch < channels; ++ch)
            {
                int dst = ChannelMap[ch];
                for (int i = 0; i < m_channel_size; ++i)
                {
                    m_output[dst] = pixels[src++];
                    dst += channels;
                }
            }
        }

        static readonly byte[] ChannelMap = { 2, 1, 0, 3 };

        void ReadPalette (int palette_size)
        {
            int colors = Math.Min (0x100, palette_size/3);
            Palette = ImageFormat.ReadPalette (m_input.AsStream, colors, PaletteFormat.Rgb);
        }

        byte[] UnpackRLE ()
        {
            var scanlines = new int[m_info.Channels, (int)m_info.Height];
            for (int ch = 0; ch < m_info.Channels; ++ch)
            {
                for (uint row = 0; row < m_info.Height; ++row)
                    scanlines[ch,row] = Binary.BigEndian (m_input.ReadInt16());
            }
            var pixels = new byte[m_info.Channels * m_channel_size];
            int dst = 0;
            for (int ch = 0; ch < m_info.Channels; ++ch)
            {
                for (uint row = 0; row < m_info.Height; ++row)
                {
                    int line_count = scanlines[ch,row];
                    int n = 0;
                    while (n < line_count)
                    {
                        int count = m_input.ReadInt8();
                        ++n;
                        if (count >= 0)
                        {
                            ++count;
                            m_input.Read (pixels, dst, count);
                            dst += count;
                            n += count;
                        }
                        else if (count > -128)
                        {
                            count = 1 - count;
                            byte color = m_input.ReadUInt8();
                            ++n;
                            for (int i = 0; i < count; ++i)
                                pixels[dst++] = color;
                        }
                    }
                }
            }
            return pixels;
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
