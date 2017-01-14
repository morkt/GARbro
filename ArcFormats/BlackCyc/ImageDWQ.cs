//! \file       ImageDWQ.cs
//! \date       Sat Aug 01 13:18:46 2015
//! \brief      Black Cyc image format.
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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.BlackCyc
{
    internal class ResourceHeader
    {
        public static readonly Regex PackTypeRe = new Regex (@"^PACKTYPE=(\d+)(A?) +$");

        public byte[] Bytes { get; private set; }
        public int PackType { get; private set; }
        public bool   AType { get; private set; }

        public static ResourceHeader Read (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40).ToArray();

            var header_string = Encoding.ASCII.GetString (header, 0x30, 0x10);
            var match = PackTypeRe.Match (header_string);
            if (!match.Success)
                return null;
            return new ResourceHeader {
                Bytes = header,
                PackType = ushort.Parse (match.Groups[1].Value),
                AType = match.Groups[2].Value.Length > 0,
            };
        }
    }

    internal class DwqMetaData : ImageMetaData
    {
        public string BaseType;
        public int    PackedSize;
        public int    PackType;
        public bool   HasAlpha;
    }

    [Export(typeof(ImageFormat))]
    public class DwqFormat : ImageFormat
    {
        public override string         Tag { get { return "DWQ"; } }
        public override string Description { get { return "Black Cyc image format"; } }
        public override uint     Signature { get { return 0; } }

        public DwqFormat ()
        {
            Signatures = new uint[] {
                0x4745504A, // JPEG
                0x20504D42, // BMP
                0x20474E50, // PNG
                0x4B434150, // PACKBMP
                0x2B504D42, // BMP+MASK
                0x50204649, // IF PACKTYPE==0  CUT THIS 64 BYTETHEN REMAKE BMP
            };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = ResourceHeader.Read (file);
            if (null == header)
                return null;
            if (Binary.AsciiEqual (header.Bytes, "IF PACKTYPE=="))
            {
                if (!Binary.AsciiEqual (header.Bytes, 0x0D, "0 ") ||
                    !Binary.AsciiEqual (header.Bytes, 0x2C, "BMP ") ||
                    header.PackType != 0 && header.PackType != 1)
                    return null;
                using (var reg = new StreamRegion (file.AsStream, 0x40, true))
                using (var bmp = new BinaryStream (reg, file.Name))
                {
                    var info = Bmp.ReadMetaData (bmp);
                    if (null == info)
                        return null;
                    return new DwqMetaData
                    {
                        Width  = info.Width,
                        Height = info.Height,
                        BPP    = info.BPP,
                        BaseType = "BMP",
                        PackedSize = (int)(file.Length-0x40),
                        PackType = header.PackType,
                        HasAlpha = header.AType,
                    };
                }
            }
            int packed_size;
            switch (header.PackType)
            {
            case 0: // BMP
            case 5: // JPEG
            case 8: // PNG
                packed_size = (int)(file.Length-0x40);
                break;

            case 2: // BMP+MASK
            case 3: // PACKBMP+MASK
            case 7: // JPEG+MASK
                packed_size = LittleEndian.ToInt32 (header.Bytes, 0x20);
                break;

            default: // unknown format
                return null;
            }
            return new DwqMetaData
            {
                Width  = LittleEndian.ToUInt32 (header.Bytes, 0x24),
                Height = LittleEndian.ToUInt32 (header.Bytes, 0x28),
                BPP = 32,
                BaseType = Encoding.ASCII.GetString (header.Bytes, 0, 0x10).TrimEnd(),
                PackedSize = packed_size,
                PackType = header.PackType,
                HasAlpha = header.AType || 7 == header.PackType || 3 == header.PackType,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DwqMetaData)info;

            BitmapSource bitmap = null;
            using (var sreg = new StreamRegion (stream.AsStream, 0x40, meta.PackedSize, true))
            using (var input = new BinaryStream (sreg, stream.Name))
            {
                switch (meta.PackType)
                {
                case 5: // JPEG
                    return Jpeg.Read (input, info);

                case 8: // PNG
                    return Png.Read (input, info);

                case 0: // BMP
                case 2: // BMP+MASK
                    bitmap = ReadFuckedUpBmpImage (input, info);
                    break;

                case 7: // JPEG+MASK
                    {
                        var decoder = new JpegBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        bitmap = decoder.Frames[0];
                        break;
                    }

                case 1:
                case 3: // PACKBMP+MASK
                    {
                        var reader = new DwqBmpReader (input, meta);
                        reader.Unpack();
                        bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height,
                                    ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                    reader.Format, reader.Palette, reader.Data, reader.Stride);
                        break;
                    }
                }
            }
            if (null == bitmap)
                throw new NotImplementedException();
            if (meta.HasAlpha)
            {
                int mask_offset = 0x40+meta.PackedSize;
                if (mask_offset != stream.Length)
                {
                    using (var mask = new StreamRegion (stream.AsStream, mask_offset, true))
                    {
                        var reader = new DwqBmpReader (mask, meta);
                        if (8 == reader.Format.BitsPerPixel) // mask should be represented as 8bpp bitmap
                        {
                            reader.Unpack();
                            var alpha = reader.Data;
                            var palette = reader.Palette.Colors;
                            for (int i = 0; i < alpha.Length; ++i)
                            {
                                var color = palette[alpha[i]];
                                int A = (color.R + color.G + color.B) / 3;
                                alpha[i] = (byte)A;
                            }
                            bitmap = ApplyAlphaChannel (bitmap, alpha);
                        }
                    }
                }
            }
            bitmap.Freeze();
            return new ImageData (bitmap, meta);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("DwqFormat.Write not implemented");
        }

        private BitmapSource ApplyAlphaChannel (BitmapSource bitmap, byte[] alpha)
        {
            if (bitmap.Format.BitsPerPixel != 32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);

            int stride = bitmap.PixelWidth * 4;
            byte[] pixels = new byte[stride * bitmap.PixelHeight];
            int asrc = 0;
            bitmap.CopyPixels (pixels, stride, 0);
            for (int dst = 3; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = alpha[asrc++];
            }
            return BitmapSource.Create (bitmap.PixelWidth, bitmap.PixelHeight,
                        ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                        PixelFormats.Bgra32, null, pixels, stride);
        }

        private BitmapSource ReadFuckedUpBmpImage (IBinaryStream file, ImageMetaData info)
        {
            var header = file.ReadHeader (0x36);
            int w = header.ToInt32 (0x12);
            int h = header.ToInt32 (0x16);
            if (w != info.Width || h != info.Height)
                throw new InvalidFormatException();

            int bpp = header.ToUInt16 (0x1c);
            PixelFormat format;
            switch (bpp)
            {
            case 32: format = PixelFormats.Bgr32; break;
            case 24: format = PixelFormats.Bgr24; break;
            case 16: format = PixelFormats.Bgr565; break;
            case 8:  format = PixelFormats.Indexed8; break;
            default: throw new NotImplementedException();
            }
            BitmapPalette palette = null;
            if (8 == bpp)
            {
                int colors = Math.Min (header.ToInt32 (0x2E), 0x100);
                palette = ImageFormat.ReadPalette (file.AsStream, colors);
            }
            int pixel_size = bpp / 8;
            int stride = ((int)info.Width * pixel_size + 3) & ~3;
            var pixels = new byte[stride * info.Height];
            if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            if (bpp >= 24)
            {
                for (int row = 0; row < pixels.Length; row += stride)
                {
                    for (int i = 2; i < stride; i += pixel_size)
                    {
                        var t = pixels[row+i];
                        pixels[row+i] = pixels[row+i-2];
                        pixels[row+i-2] = t;
                    }
                }
            }
            return BitmapSource.Create ((int)info.Width, (int)info.Height,
                                        ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                        format, palette, pixels, stride);
        }
    }

    internal class DwqBmpReader
    {
        Stream      m_input;
        byte[]      m_pixels;
        int         m_width;
        int         m_height;

        public byte[]           Data { get { return m_pixels; } }
        public int            Stride { get; private set; }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public DwqBmpReader (Stream input, DwqMetaData info)
        {
            m_input = input;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            var header = new byte[0x36];
            if (header.Length != m_input.Read (header, 0, header.Length))
                throw new InvalidFormatException();
            int w = LittleEndian.ToInt32 (header, 0x12);
            int h = LittleEndian.ToInt32 (header, 0x16);
            if (w != m_width || Math.Abs (h) != m_height)
                throw new InvalidFormatException();

            int bpp = LittleEndian.ToUInt16 (header, 0x1C);
            switch (bpp)
            {
            case 8:     Format = PixelFormats.Indexed8; Stride = m_width; break;
            case 16:    Format = PixelFormats.Bgr565;   Stride = m_width*2; break;
            case 24:    Format = PixelFormats.Bgr24;    Stride = m_width*3; break;
            case 32:    Format = PixelFormats.Bgr32;    Stride = m_width*4; break;
            default:    throw new InvalidFormatException();
            }
            if (8 == bpp)
            {
                int colors = Math.Min (LittleEndian.ToInt32 (header, 0x2E), 0x100);
                if (0 == colors)
                    colors = 0x100;
                Palette = ImageFormat.ReadPalette (m_input, colors);
            }
            uint data_position = LittleEndian.ToUInt32 (header, 0xA);
            m_input.Position = data_position;
            m_pixels = new byte[Stride*m_height];
        }

        public void Unpack () // sub_408990
        {
            var prev_line = new byte[Stride];
            int dst = 0;
            for (int y = 0; y < m_height; ++y)
            {
                for (int x = 0; x < Stride; )
                {
                    int b = m_input.ReadByte();
                    if (0 != b)
                    {
                        if (-1 == b)
                            throw new EndOfStreamException();
                        m_pixels[dst + x++] = (byte)b;
                    }
                    else
                    {
                        int count = m_input.ReadByte();
                        if (-1 == count)
                            throw new EndOfStreamException();
                        for (int i = 0; i < count; ++i)
                            m_pixels[dst + x++] = 0;
                    }
                }
                for (int i = 0; i < Stride; ++i)
                {
                    m_pixels[dst] ^= prev_line[i];
                    prev_line[i] = m_pixels[dst++];
                }
            }
        }
    }
}
