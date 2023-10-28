//! \file       ImageMAI.cs
//! \date       Sun May 03 10:26:35 2015
//! \brief      MAI image formats implementation.
//
// Copyright (C) 2015-2016 by morkt
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
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.MAI
{
    internal class CmMetaData : ImageMetaData
    {
        public  int Colors;
        public bool IsCompressed;
        public uint DataOffset;
        public uint DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class CmFormat : ImageFormat
    {
        public override string         Tag { get { return "CM/MAI"; } }
        public override string Description { get { return "MAI image format"; } }
        public override uint     Signature { get { return 0; } }

        public CmFormat ()
        {
            Extensions = new string[] { "cmp" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("CmFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            if ('C' != header[0] || 'M' != header[1])
                return null;
            if (1 != header[0x0E])
                return null;
            uint size = LittleEndian.ToUInt32 (header, 2);
            if (size != stream.Length)
                return null;
            var info = new CmMetaData();
            info.Width = LittleEndian.ToUInt16 (header, 6);
            info.Height = LittleEndian.ToUInt16 (header, 8);
            info.Colors = LittleEndian.ToUInt16 (header, 0x0A);
            info.BPP = header[0x0C];
            info.IsCompressed = 0 != header[0x0D];
            info.DataOffset = LittleEndian.ToUInt32 (header, 0x10);
            info.DataLength = LittleEndian.ToUInt32 (header, 0x14);
            if (info.DataLength > size)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new Reader (stream.AsStream, (CmMetaData)info);
            reader.Unpack();
            return ImageData.CreateFlipped (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
        }

        internal class Reader
        {
            private Stream  m_input;
            private int     m_width;
            private int     m_height;
            private int     m_pixel_size;
            private bool    m_compressed;
            private int     m_data_length;
            private byte[]  m_pixels;
            
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_pixels; } }
            public int            Stride { get { return m_width * m_pixel_size; } }

            public Reader (Stream stream, CmMetaData info)
            {
                m_input = stream;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_pixel_size = info.BPP/8;
                m_compressed = info.IsCompressed;
                m_data_length = (int)info.DataLength;
                switch (m_pixel_size)
                {
                case 1: Format = PixelFormats.Indexed8; break;
                case 3: Format = PixelFormats.Bgr24; break;
                case 4: Format = PixelFormats.Bgr32; break;
                default: throw new InvalidFormatException ("Invalid color depth");
                }
                if (info.Colors > 0)
                {
                    m_input.Position = 0x20;
                    Palette = ImageFormat.ReadPalette (m_input, info.Colors, PaletteFormat.Bgr);
                }
                m_input.Position = info.DataOffset;
                int size = info.IsCompressed ? m_width*m_height*m_pixel_size : (int)info.DataLength;
                m_pixels = new byte[size];
            }

            public void Unpack ()
            {
                if (m_compressed)
                    RleDecoder.Unpack (m_input, m_data_length, m_pixels, m_pixel_size);
                else
                    m_input.Read (m_pixels, 0, m_pixels.Length);
            }
        }
    }

    internal class AmMetaData : CmMetaData
    {
        public uint MaskWidth;
        public uint MaskHeight;
        public uint MaskOffset;
        public uint MaskLength;
        public bool IsMaskCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class AmFormat : ImageFormat
    {
        public override string         Tag { get { return "AM/MAI"; } }
        public override string Description { get { return "MAI image with alpha-channel"; } }
        public override uint     Signature { get { return 0; } }

        public AmFormat ()
        {
            Extensions = new string[] { "amp", "ami" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("AmFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x30);
            if ('A' != header[0] || 'M' != header[1])
                return null;
            uint size = header.ToUInt32 (2);
            if (size != stream.Length)
                return null;
            int am_type = header[0x16];
            if (am_type != 2 && am_type != 1 || header[0x18] != 1)
                return null;
            var info = new AmMetaData {
                Width = header.ToUInt16 (6),
                Height = header.ToUInt16 (8),
                MaskWidth = header.ToUInt16 (0x0A),
                MaskHeight = header.ToUInt16 (0x0C),
                Colors = header.ToUInt16 (0x12),
                BPP = header[0x14],
                IsCompressed = 0 != header[0x15],
                DataOffset = header.ToUInt32 (0x1A),
                DataLength = header.ToUInt32 (0x1E),
                MaskOffset = header.ToUInt32 (0x22),
                MaskLength = header.ToUInt32 (0x26),
                IsMaskCompressed = 0 != header[0x2A],
            };
            if (checked(info.DataLength + info.MaskLength) > size)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var reader = new Reader (stream.AsStream, (AmMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            private Stream  m_input;
            private AmMetaData m_info;
            private int     m_width;
            private int     m_height;
            private int     m_pixel_size;
            private byte[]  m_output;
            private byte[]  m_alpha;
            private byte[]  m_pixels;
            
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_pixels; } }

            public Reader (Stream stream, AmMetaData info)
            {
                m_input = stream;
                m_info = info;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_pixel_size = info.BPP/8;
                if (m_pixel_size != 3 && m_pixel_size != 4 && m_pixel_size != 1)
                    throw new InvalidFormatException ("Invalid color depth");
                Format = PixelFormats.Bgra32;
                int size = info.IsCompressed ? m_width*m_height*m_pixel_size : (int)info.DataLength;
                m_output = new byte[size];
                uint mask_size = info.IsMaskCompressed ? info.MaskWidth*info.MaskHeight : info.MaskLength;
                m_alpha = new byte[mask_size];
                m_pixels = new byte[m_width*m_height*4];
            }

            static readonly Color Default8bppTransparencyColor = Color.FromRgb (0, 0xFE, 0);

            public void Unpack ()
            {
                if (m_info.Colors > 0)
                {
                    m_input.Position = 0x30;
                    Palette = ImageFormat.ReadPalette (m_input, m_info.Colors, PaletteFormat.Bgr);
                }
                m_input.Position = m_info.DataOffset;
                if (m_info.IsCompressed)
                    RleDecoder.Unpack (m_input, (int)m_info.DataLength, m_output, m_pixel_size);
                else
                    m_input.Read (m_output, 0, m_output.Length);
                m_input.Position = m_info.MaskOffset;
                if (m_info.IsMaskCompressed)
                    RleDecoder.Unpack (m_input, (int)m_info.MaskLength, m_alpha, 1);
                else
                    m_input.Read (m_alpha, 0, m_alpha.Length);

                Action<int, int, byte> copy_pixel;
                if (m_pixel_size > 1)
                    copy_pixel = (src, dst, alpha) => {
                        m_pixels[dst]   = m_output[src];
                        m_pixels[dst+1] = m_output[src+1];
                        m_pixels[dst+2] = m_output[src+2];
                        m_pixels[dst+3] = alpha;
                    };
                else
                {
                    const int alphaScale = 0x11;
                    var alphaColor = Color.FromRgb (0, 0xFE, 0);
                    copy_pixel = (src, dst, alpha) => {
                        var color = Palette.Colors[m_output[src]];
                        if (Default8bppTransparencyColor == color)
                            alpha = 0;
                        else if (0 == alpha)
                            alpha = 0xFF;
                        else
                            alpha *= alphaScale;
                        m_pixels[dst]   = color.B;
                        m_pixels[dst+1] = color.G;
                        m_pixels[dst+2] = color.R;
                        m_pixels[dst+3] = alpha;
                    };
                }
                int src_stride = m_width * m_pixel_size;
                for (int y = 0; y < m_height; ++y)
                {
                    int dst_line = y*m_width*4;
                    int src_line = (m_height-1-y)*src_stride;;
                    for (int x = 0; x < m_width; ++x)
                    {
                        copy_pixel (src_line, dst_line, m_alpha[y*m_width+x]);
                        src_line += m_pixel_size;
                        dst_line += 4;
                    }
                }
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class MaskFormat : ImageFormat
    {
        public override string         Tag { get { return "MSK/MAI"; } }
        public override string Description { get { return "MAI indexed image format"; } }
        public override uint     Signature { get { return 0; } }

        public MaskFormat ()
        {
            Extensions = new string[] { "msk" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("MaskFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint size = file.ReadUInt32();
            if (size != file.Length)
                return null;
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            int compressed = file.ReadInt32();
            if (compressed > 1 || 0 == compressed && (width*height + 0x410) != size)
                return null;
            return new CmMetaData {
                Width = width,
                Height = height,
                BPP = 8,
                IsCompressed = 1 == compressed,
                DataOffset = 0x10,
                DataLength = size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (CmMetaData)info;
            stream.Position = meta.DataOffset;
            var palette = ReadPalette (stream.AsStream, 0x100, PaletteFormat.BgrX);

            var pixels = new byte[info.Width*info.Height];
            if (meta.IsCompressed)
            {
                int packed_size = (int)(stream.Length - meta.DataOffset);
                RleDecoder.Unpack (stream.AsStream, packed_size, pixels, 1);
            }
            else if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException();

            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
        }
    }

    internal class RleDecoder
    {
        static public void Unpack (Stream input, int input_size, byte[] output, int pixel_size)
        {
            int read = 0;
            int dst = 0;
            while (read < input_size && dst < output.Length)
            {
                int code = input.ReadByte();
                ++read;
                if (-1 == code)
                    throw new InvalidFormatException ("Unexpected end of file");
                if (0x80 == code)
                    throw new InvalidFormatException ("Invalid run-length code");
                if (code < 0x80)
                {
                    int count = Math.Min (code * pixel_size, output.Length - dst);
                    if (count != input.Read (output, dst, count))
                        break;
                    read += count;
                    dst  += count;
                }
                else
                {
                    int count = code & 0x7f;
                    if (pixel_size != input.Read (output, dst, pixel_size))
                        break;
                    read += pixel_size;
                    int src = dst;
                    dst  += pixel_size;
                    count = Math.Min ((count - 1) * pixel_size, output.Length - dst);
                    Binary.CopyOverlapped (output, src, dst, count);
                    dst += count;
                }
            }
        }
    }
}
