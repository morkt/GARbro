//! \file       ImageMAI.cs
//! \date       Sun May 03 10:26:35 2015
//! \brief      MAI image formats implementation.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.MAI
{
    internal class CmpMetaData : ImageMetaData
    {
        public  int Colors;
        public bool IsCompressed;
        public uint DataOffset;
        public uint DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class CmpFormat : ImageFormat
    {
        public override string         Tag { get { return "CMP/MAI"; } }
        public override string Description { get { return "MAI image format"; } }
        public override uint     Signature { get { return 0; } }

        public CmpFormat ()
        {
            Extensions = new string[] { "cmp" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("CmpFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            if ('C' != stream.ReadByte() || 'M' != stream.ReadByte())
                return null;
            var header = new byte[0x1e];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (1 != header[0x0c])
                return null;
            uint size = LittleEndian.ToUInt32 (header, 0);
            if (size != stream.Length)
                return null;
            var info = new CmpMetaData();
            info.Width = LittleEndian.ToUInt16 (header, 4);
            info.Height = LittleEndian.ToUInt16 (header, 6);
            info.Colors = LittleEndian.ToUInt16 (header, 8);
            info.BPP = header[0x0a];
            info.IsCompressed = 0 != header[0x0b];
            info.DataOffset = LittleEndian.ToUInt32 (header, 0x0e);
            info.DataLength = LittleEndian.ToUInt32 (header, 0x12);
            if (info.DataLength > size)
                return null;
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as CmpMetaData;
            if (null == meta)
                throw new ArgumentException ("CmpFormat.Read should be supplied with CmpMetaData", "info");

            var reader = new Reader (stream, meta);
            reader.Unpack();
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height,
                ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                reader.Format, reader.Palette, reader.Data, reader.Stride);
            var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
            flipped.Freeze();
            return new ImageData (flipped, info);
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

            public Reader (Stream stream, CmpMetaData info)
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
                m_input.Position = info.DataOffset;
                if (info.Colors > 0)
                    Palette = RleDecoder.ReadPalette (m_input, info.Colors, 3);
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

    internal class AmiMetaData : CmpMetaData
    {
        public uint MaskWidth;
        public uint MaskHeight;
        public uint MaskOffset;
        public uint MaskLength;
        public bool IsMaskCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class AmiFormat : ImageFormat
    {
        public override string         Tag { get { return "AM/MAI"; } }
        public override string Description { get { return "MAI image with alpha-channel"; } }
        public override uint     Signature { get { return 0; } }

        public AmiFormat ()
        {
            Extensions = new string[] { "am", "ami" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("AmiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            if ('A' != stream.ReadByte() || 'M' != stream.ReadByte())
                return null;
            var header = new byte[0x30];
            if (0x2e != stream.Read (header, 2, 0x2e))
                return null;
            uint size = LittleEndian.ToUInt32 (header, 2);
            if (size != stream.Length)
                return null;
            int am_type = header[0x16];
            if (am_type != 2 && am_type != 1 || header[0x18] != 1)
                return null;
            var info = new AmiMetaData();
            info.Width = LittleEndian.ToUInt16 (header, 6);
            info.Height = LittleEndian.ToUInt16 (header, 8);
            info.MaskWidth = LittleEndian.ToUInt16 (header, 0x0a);
            info.MaskHeight = LittleEndian.ToUInt16 (header, 0x0c);
            info.Colors = LittleEndian.ToUInt16 (header, 0x12);
            info.BPP = header[0x14];
            info.IsCompressed = 0 != header[0x15];
            info.DataOffset = LittleEndian.ToUInt32 (header, 0x1a);
            info.DataLength = LittleEndian.ToUInt32 (header, 0x1e);
            info.MaskOffset = LittleEndian.ToUInt32 (header, 0x22);
            info.MaskLength = LittleEndian.ToUInt32 (header, 0x26);
            info.IsMaskCompressed = 0 != header[0x2a];
            if (checked(info.DataLength + info.MaskLength) > size)
                return null;
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as AmiMetaData;
            if (null == meta)
                throw new ArgumentException ("AmiFormat.Read should be supplied with AmiMetaData", "info");

            var reader = new Reader (stream, meta);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            private Stream  m_input;
            private AmiMetaData m_info;
            private int     m_width;
            private int     m_height;
            private int     m_pixel_size;
            private bool    m_compressed;
            private byte[]  m_output;
            private byte[]  m_alpha;
            private byte[]  m_pixels;
            
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_pixels; } }

            public Reader (Stream stream, AmiMetaData info)
            {
                m_input = stream;
                m_info = info;
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_pixel_size = info.BPP/8;
                if (m_pixel_size != 3 && m_pixel_size != 4)
                    throw new InvalidFormatException ("Invalid color depth");
                Format = PixelFormats.Bgra32;
                int size = info.IsCompressed ? m_width*m_height*m_pixel_size : (int)info.DataLength;
                m_output = new byte[size];
                uint mask_size = info.IsMaskCompressed ? info.MaskWidth*info.MaskHeight : info.MaskLength;
                m_alpha = new byte[mask_size];
                m_pixels = new byte[m_width*m_height*4];
            }

            public void Unpack ()
            {
                m_input.Position = m_info.DataOffset;
                if (m_info.Colors > 0)
                    Palette = RleDecoder.ReadPalette (m_input, m_info.Colors, 3);
                if (m_info.IsCompressed)
                    RleDecoder.Unpack (m_input, (int)m_info.DataLength, m_output, m_pixel_size);
                else
                    m_input.Read (m_output, 0, m_output.Length);
                m_input.Position = m_info.MaskOffset;
                if (m_info.IsMaskCompressed)
                    RleDecoder.Unpack (m_input, (int)m_info.MaskLength, m_alpha, 1);
                else
                    m_input.Read (m_alpha, 0, m_alpha.Length);

                int stride = m_width * m_pixel_size;
                for (int y = 0; y < m_height; ++y)
                {
                    int dst_line = y*m_width;
                    int src_line = (m_height-1-y)*m_width;
                    for (int x = 0; x < m_width; ++x)
                    {
                        m_pixels[(dst_line+x)*4] = m_output[(src_line+x)*m_pixel_size];
                        m_pixels[(dst_line+x)*4+1] = m_output[(src_line+x)*m_pixel_size+1];
                        m_pixels[(dst_line+x)*4+2] = m_output[(src_line+x)*m_pixel_size+2];
                        m_pixels[(dst_line+x)*4+3] = m_alpha[dst_line+x];
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
            throw new NotImplementedException ("AmiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                uint size = input.ReadUInt32();
                if (size != stream.Length)
                    return null;
                uint width = input.ReadUInt32();
                uint height = input.ReadUInt32();
                if ((width*height + 0x410) != size)
                    return null;
                return new ImageMetaData {
                    Width = width,
                    Height = height,
                    BPP = 8
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Position = 0x10;
            var palette = RleDecoder.ReadPalette (stream, 0x100, 4);

            byte[] pixels = new byte[info.Width*info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException();

            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
        }
    }

    internal class RleDecoder
    {
        public static BitmapPalette ReadPalette (Stream input, int colors, int color_size)
        {
            var palette_data = new byte[colors*color_size];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new InvalidFormatException();
            var palette = new Color[colors];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * color_size;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return new BitmapPalette (palette);
        }

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
