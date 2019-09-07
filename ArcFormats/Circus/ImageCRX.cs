//! \file       ImageCRX.cs
//! \date       Mon Jun 15 15:14:59 2015
//! \brief      Circus image format.
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Circus
{
    internal class CrxMetaData : ImageMetaData
    {
        public int Compression;
        public int CompressionFlags;
        public int Colors;
        public int Mode;
    }

    [Export(typeof(ImageFormat))]
    public class CrxFormat : ImageFormat
    {
        public override string         Tag { get { return "CRX"; } }
        public override string Description { get { return "Circus image format"; } }
        public override uint     Signature { get { return 0x47585243; } } // 'CRXG'
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            int compression = header.ToUInt16 (0xC);
            if (compression < 1 || compression > 3)
                return null;
            int depth = header.ToInt16 (0x10);
            var info = new CrxMetaData
            {
                Width = header.ToUInt16 (8),
                Height = header.ToUInt16 (10),
                OffsetX = header.ToInt16 (4),
                OffsetY = header.ToInt16 (6),
                BPP = 0 == depth ? 24 : 1 == depth ? 32 : 8,
                Compression = compression,
                CompressionFlags = header.ToUInt16 (0xE),
                Colors = depth,
                Mode = header.ToUInt16 (0x12),
            };
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new Reader (stream, (CrxMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            var header = new byte[0x14];

            var depth = (short)(24 == image.BPP ? 0 : 32 == image.BPP ? 1 : 2);
            var compression = (ushort)3;
            var flags = (ushort)17;
            var mode = (ushort)0;

            using (var memeStream = new MemoryStream(header))
            {
                using (var binaryWriter = new BinaryWriter(memeStream))
                {
                    binaryWriter.Write(Signature);
                    binaryWriter.Write((ushort)image.OffsetX);
                    binaryWriter.Write((ushort)image.OffsetY);
                    binaryWriter.Write((ushort)image.Width);
                    binaryWriter.Write((ushort)image.Height);
                    binaryWriter.Write(compression);
                    binaryWriter.Write(flags);
                    binaryWriter.Write(depth);
                    binaryWriter.Write(mode);
                }
            }

            var metaData = ReadMetaData(BinaryStream.FromArray(header,""));

            var bitmap = image.Bitmap;
            var pixelFormat = CheckFormat(image.BPP);

            int stride = (int)(image.Width * pixelFormat.BitsPerPixel / 8 + 3) & ~3;

            if (pixelFormat != bitmap.Format )
            {
                var converted_bitmap = new FormatConvertedBitmap();
                converted_bitmap.BeginInit();
                converted_bitmap.Source = image.Bitmap;
                converted_bitmap.DestinationFormat = pixelFormat;
                converted_bitmap.EndInit();
                bitmap = converted_bitmap;
            }

            var data = new byte[image.Height * stride];
            var row_data = new byte[stride];
            var rect = new Int32Rect(0, 0, (int)image.Width, 1);

            for (uint row = 0; row < image.Height; ++row)
            {
                bitmap.CopyPixels(rect, row_data, stride, 0);
                rect.Y++;
                row_data.CopyTo(data, row * stride);
            }

            using (var binaryWriter = new BinaryWriter(file))
            {
                binaryWriter.Write(header);
                using (var writer = new Writer(data, 
                    binaryWriter,(CrxMetaData)metaData ))
                {
                    writer.Write();
                }
            }
        }

        private static PixelFormat CheckFormat(int bpp)
        {
            switch (bpp)
            {
                case 24: return PixelFormats.Bgr24;
                case 32: return PixelFormats.Bgra32;
                case 8: return PixelFormats.Indexed8;
                default: throw new InvalidFormatException();
            }
        }

        internal sealed class Reader : IDisposable
        {
            IBinaryStream   m_input;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_stride;
            int             m_bpp;
            int             m_compression;
            int             m_flags;
            int             m_mode;

            public byte[]           Data { get { return m_output; } }
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public int            Stride { get { return m_stride; } }

            public Reader (IBinaryStream input, CrxMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_compression = info.Compression;
                m_flags = info.CompressionFlags;
                m_mode = info.Mode;
                Format = CheckFormat(m_bpp);
                m_stride = (m_width * m_bpp / 8 + 3) & ~3;
                m_output = new byte[m_height*m_stride];
                m_input = input;
                m_input.Position = 0x14;
                if (8 == m_bpp)
                    ReadPalette (info.Colors);
            }

            private void ReadPalette (int colors)
            {
                int color_size = 0x102 == colors ? 4 : 3;
                if (colors > 0x100)
                {
                    colors = 0x100;
                }
                int palette_size = colors * color_size;
                var palette_data = new byte[palette_size];
                if (palette_size != m_input.Read (palette_data, 0, palette_size))
                    throw new InvalidFormatException();
                var palette = new Color[colors];
                int color_pos = 0;
                for (int i = 0; i < palette.Length; ++i)
                {
                    byte r = palette_data[color_pos];
                    byte g = palette_data[color_pos+1];
                    byte b = palette_data[color_pos+2];
                    if (0xff == b && 0 == g && 0xff == r)
                        g = 0xff;
                    palette[i] = Color.FromRgb (r, g, b);
                    color_pos += color_size;
                }
                Palette = new BitmapPalette (palette);
            }

            public void Unpack (bool is_diff = false)
            {
                if (m_compression >= 3)
                {
                    int count = m_input.ReadInt32();
                    m_input.Seek (count * 0x10, SeekOrigin.Current);
                }
                if (0 != (m_flags & 0x10))
                {
                    m_input.ReadInt32(); // compressed_size
                }
                if (1 == m_compression)
                    UnpackV1();
                else
                    UnpackV2();

                if (32 == m_bpp && m_mode != 1)
                {
                    int alpha_flip = 2 == m_mode ? 0 : 0xFF;
                    int line = 0;
                    for (int h = 0; h < m_height; h++)
                    {
                        for (int w = 0;	w < m_width; w++)
                        {
                            int pixel = line + w * 4;
                            var alpha = m_output[pixel];
                            var b = m_output[pixel+1];
                            var g = m_output[pixel+2];
                            var r = m_output[pixel+3];
                            m_output[pixel]   = b;
                            m_output[pixel+1] = g;
                            m_output[pixel+2] = r;
                            m_output[pixel+3] = (byte)(alpha ^ alpha_flip);
                        }
                        line += m_stride;
                    }
                }
            }

            private void UnpackV1 ()
            {
                byte[] window = new byte[0x10000];
                int flag = 0;
                int win_pos = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    flag >>= 1;
                    if (0 == (flag & 0x100))
                        flag = m_input.ReadUInt8() | 0xff00;

                    if (0 != (flag & 1))
                    {
                        byte dat = m_input.ReadUInt8();
                        window[win_pos++] = dat;
                        win_pos &= 0xffff;
                        m_output[dst++] = dat;
                    }
                    else
                    {
                        byte control = m_input.ReadUInt8();
                        int count, offset;

                        if (control >= 0xc0)
                        {
                            offset = ((control & 3) << 8) | m_input.ReadUInt8();
                            count = 4 + ((control >> 2) & 0xf);
                        }
                        else if (0 != (control & 0x80))
                        {
                            offset = control & 0x1f;
                            count = 2 + ((control >> 5) & 3);
                            if (0 == offset)
                                offset = m_input.ReadUInt8();
                        }
                        else if (0x7f == control)
                        {
                            count = 2 + m_input.ReadUInt16();
                            offset = m_input.ReadUInt16();
                        }
                        else
                        {
                            offset = m_input.ReadUInt16();
                            count = control + 4;
                        }
                        offset = win_pos - offset;
                        for (int k = 0; k < count && dst < m_output.Length; k++)
                        {
                            offset &= 0xffff;
                            byte dat = window[offset++];
                            window[win_pos++] = dat;
                            win_pos &= 0xffff;
                            m_output[dst++] = dat;
                        }
                    }
                }
            }

            private void UnpackV2 ()
            {
                int pixel_size = m_bpp / 8;
                int src_stride = m_width * pixel_size;
                using (var zlib = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
                using (var src = new BinaryReader (zlib))
                {
                    if (m_bpp >= 24)
                    {
                        for (int y = 0; y < m_height; ++y)
                        {
                            byte ctl = src.ReadByte();
                            int dst = y * m_stride;
                            int prev_row = dst - m_stride;
                            switch (ctl)
                            {
                            case 0:
                                src.Read (m_output, dst, pixel_size);
                                for (int x = pixel_size; x < src_stride; ++x)
                                    m_output[dst+x] = (byte)(src.ReadByte() + m_output[dst+x - pixel_size]);
                                break;
                            case 1:
                                for (int x = 0; x < src_stride; ++x)
                                    m_output[dst+x] = (byte)(src.ReadByte() + m_output[prev_row+x]);
                                break;
                            case 2:
                                src.Read (m_output, dst, pixel_size);
                                for (int x = pixel_size; x < src_stride; ++x)
                                    m_output[dst+x] = (byte)(src.ReadByte() + m_output[prev_row+x - pixel_size]);
                                break;
                            case 3:
                                for (int x = src_stride - pixel_size; x > 0; --x)
                                    m_output[dst++] = (byte)(src.ReadByte() + m_output[prev_row++ + pixel_size]);
                                src.Read (m_output, dst, pixel_size);
                                break;
                            case 4:
                                for (int i = 0; i < pixel_size; ++i)
                                {
                                    int w = m_width;
                                    byte val = src.ReadByte();
                                    while (w > 0)
                                    {
                                        m_output[dst] = val;
                                        dst += pixel_size;
                                        if (0 == --w)
                                            break;
                                        byte next = src.ReadByte();
                                        if (val == next)
                                        {
                                            int count = src.ReadByte();
                                            for (int j = 0; j < count; ++j)
                                            {
                                                m_output[dst] = val;
                                                dst += pixel_size;
                                            }
                                            w -= count;
                                            if (w > 0)
                                                val = src.ReadByte();
                                        }
                                        else
                                            val = next;
                                    }
                                    dst -= src_stride - 1;
                                }
                                break;
                            default:
                                break;
                            }
                        }
                    }
                    else
                    {
                        int dst = 0;
                        for (int y = 0; y < m_height; ++y)
                        {
                            src.Read (m_output, dst, src_stride);
                            dst += m_stride;
                        }
                    }
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
            }
            #endregion
        }

        internal sealed class Writer : IDisposable
        {
            BinaryWriter m_output;
            byte[] m_input;
            int m_width;
            int m_height;
            int m_stride;
            int m_bpp;
            int m_compression;
            int m_flags;
            int m_mode;

            public byte[] Data { get { return m_input; } }
            public PixelFormat Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public int Stride { get { return m_stride; } }

            public Writer(byte[] input,
                BinaryWriter output,
                CrxMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_compression = info.Compression;
                m_flags = info.CompressionFlags;
                m_mode = info.Mode;
                Format = CheckFormat(m_bpp);
                m_stride = (m_width * m_bpp / 8 + 3) & ~3;

                m_input = input;
                m_output = output;

                m_output.Seek(0x14, SeekOrigin.Begin);

                if (8 == m_bpp)
                    ReadPalette(info.Colors);
            }

            private void ReadPalette(int colors)
            {
                throw new NotImplementedException();
            }


            public void Write(bool isDiff = false)
            {
                int compressed_size_position = 40;

                if (m_compression >= 3)
                {
                    var count = 1;
                    m_output.Write(count);
                    m_output.Seek(count * 0x10, SeekOrigin.Current);

                }
                if (0 != (m_flags & 0x10))
                {
                    compressed_size_position = (int) m_output.BaseStream.Position + 4;
                    m_output.Seek(compressed_size_position, SeekOrigin.Begin);
                }

                if (32 == m_bpp && m_mode != 1)
                {
                    int alpha_flip = 2 == m_mode ? 0 : 0xFF;
                    int line = 0;
                    for (int h = 0; h < m_height; h++)
                    {
                        for (int w = 0; w < m_width; w++)
                        {
                            int pixel = line + w * 4;

                            var b = m_input[pixel];
                            var g = m_input[pixel + 1];
                            var r = m_input[pixel + 2];
                            var alpha = m_input[pixel + 3];

                            m_input[pixel] = (byte)(alpha ^ alpha_flip);
                            m_input[pixel + 1] = b;
                            m_input[pixel + 2] = g;
                            m_input[pixel + 3] = r;

                        }
                        line += m_stride;
                    }
                }

                if (1 == m_compression)
                    WriteV1();
                else
                    WriteV2();

                m_output.Seek(compressed_size_position - 4, SeekOrigin.Begin);
                var compressed_size = (int)m_output.BaseStream.Length; // compressed_size
                m_output.Write(compressed_size);
            }

            private void WriteV2()
            {
                int pixel_size = m_bpp / 8;
                int src_stride = m_width * pixel_size;
                m_output.Flush();

                using (var zlib = new ZLibStream(m_output.BaseStream, CompressionMode.Compress, true))
                using (var output = new BinaryWriter(zlib))
                {

                    if (m_bpp >= 24)
                    {
                        for (int y = 0; y < m_height; ++y)
                        {

                            byte ctl = 0;
                            output.Write(ctl);

                            int dst = y * m_stride;
                            int prev_row = dst - m_stride;
                            switch (ctl)
                            {
                                case 0:
                                    output.Write(m_input, dst, pixel_size);
                                    for (int x = pixel_size; x < src_stride; ++x)
                                    {
                                        output.Write((byte)(m_input[dst + x] - m_input[dst + x - pixel_size]));
                                    }
                                    break;
                                case 1:
                                    for (int x = 0; x < src_stride; ++x)
                                    {
                                        output.Write((byte)(m_input[dst + x] - m_input[prev_row + x]));
                                    }
                                    break;
                                case 2:
                                    output.Write(m_input, dst, pixel_size);

                                    for (int x = pixel_size; x < src_stride; ++x)
                                    {
                                        output.Write((byte)(m_input[dst + x] - m_input[prev_row + x - pixel_size]));
                                    }
                                    break;
                                case 3:
                                    for (int x = src_stride - pixel_size; x > 0; --x)
                                    {
                                        output.Write((byte)(m_input[dst++] - m_input[prev_row++ + pixel_size]));
                                    }
                                    output.Write(m_input, dst, pixel_size);
                                    break;
                                case 4:
                                    for (int i = 0; i < pixel_size; ++i)
                                    {
                                        int w = m_width;
                                        byte val = m_input[dst];
                                        output.Write(val);
                                        while (w > 0)
                                        {
                                            dst += pixel_size;
                                            if (0 == --w)
                                                break;

                                            byte next = m_input[dst];
                                            output.Write(next);

                                            if (val == next)
                                            {
                                                var count = 255;

                                                output.Write(count);

                                                dst += pixel_size * count;
                                                w -= count;

                                                if (w > 0)
                                                {
                                                    val = m_input[dst];
                                                    output.Write(val);
                                                }

                                            }
                                            else
                                            {
                                                val = next;
                                            }

                                        }
                                        dst -= src_stride - 1;
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    else
                    {
                        int dst = 0;
                        for (int y = 0; y < m_height; ++y)
                        {
                            m_output.Write(m_input, dst, src_stride);
                            dst += m_stride;
                        }
                    }
                }

            }

            private void WriteV1()
            {
                throw new NotImplementedException ("CrxFormat.Write version 1 not implemented");
            }

            #region IDisposable Members
            public void Dispose()
            {
            }
            #endregion
        }
    }
}
