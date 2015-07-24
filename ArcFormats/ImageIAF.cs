//! \file       ImageIAF.cs
//! \date       Sun May 31 21:17:54 2015
//! \brief      IAF image format.
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
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Triangle
{
    internal class IafMetaData : ImageMetaData
    {
        public int DataOffset;
        public int PackedSize;
        public int UnpackedSize;
        public int PackType;
    }

    [Export(typeof(ImageFormat))]
    public class IafFormat : BmpFormat
    {
        public override string         Tag { get { return "IAF"; } }
        public override string Description { get { return "Triangle compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x14];
            if (12 != stream.Read (header, 0, 12))
                return null;
            int data_offset;
            int packed_size = LittleEndian.ToInt32 (header, 1);
            int x, y, unpacked_size;
            if (5+packed_size+0x14 == stream.Length)
            {
                data_offset = 5;
                stream.Seek (-0x14, SeekOrigin.End);
                if (0x14 != stream.Read (header, 0, 0x14))
                    return null;
                x = LittleEndian.ToInt32 (header, 0);
                y = LittleEndian.ToInt32 (header, 4);
                unpacked_size = LittleEndian.ToInt32 (header, 0x10);
            }
            else
            {
                data_offset = 12;
                x = LittleEndian.ToInt32 (header, 0);
                y = LittleEndian.ToInt32 (header, 4);
                unpacked_size = LittleEndian.ToInt32 (header, 8);
                packed_size = (int)stream.Length-12;
            }
            if (Math.Abs (x) > 4096 || Math.Abs (y) > 4096)
                return null;
            int pack_type = (unpacked_size >> 30) & 3;
            unpacked_size &= (int)~0xC0000000;
            stream.Position = data_offset;
            byte[] bmp;
            if (0 == pack_type)
            {
                using (var reader = new LzssReader (stream, (int)stream.Length-12, 0x26))
                {
                    reader.Unpack();
                    bmp = reader.Data;
                }
            }
            else if (2 == pack_type)
            {
                using (var reader = new RleReader (stream, (int)stream.Length-12, 0x26))
                {
                    reader.Unpack();
                    bmp = reader.Data;
                }
            }
            else if (1 == pack_type)
            {
                bmp = new byte[0x26];
                stream.Read (bmp, 0, bmp.Length);
            }
            else
            {
                return null;
            }
            if (bmp[0] != 'B' && bmp[0] != 'C' || bmp[1] != 'M')
                return null;
            int width = LittleEndian.ToInt32 (bmp, 0x12);
            int height = LittleEndian.ToInt32 (bmp, 0x16);
            int bpp = LittleEndian.ToInt16 (bmp, 0x1c);
            return new IafMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                OffsetX = x,
                OffsetY = y,
                BPP = bpp,
                DataOffset = data_offset,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
                PackType = pack_type,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as IafMetaData;
            if (null == meta)
                throw new ArgumentException ("IafFormat.Read should be supplied with IafMetaData", "info");

            stream.Position = meta.DataOffset;
            byte[] bitmap;
            if (2 == meta.PackType)
            {
                using (var reader = new RleReader (stream, meta.PackedSize, meta.UnpackedSize))
                {
                    reader.Unpack();
                    bitmap = reader.Data;
                }
            }
            else if (0 == meta.PackType)
            {
                using (var reader = new LzssReader (stream, meta.PackedSize, meta.UnpackedSize))
                {
                    reader.Unpack();
                    bitmap = reader.Data;
                }
            }
            else
            {
                bitmap = new byte[meta.UnpackedSize];
                if (bitmap.Length != stream.Read (bitmap, 0, bitmap.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
            }
            if ('C' == bitmap[0])
            {
                bitmap[0] = (byte)'B';
                if (info.BPP > 8)
                    bitmap = ConvertCM (bitmap, (int)info.Width, (int)info.Height, info.BPP);
            }
            if (meta.BPP >= 24) // currently alpha channel could be applied to 24+bpp bitmaps only
            {
                try
                {
                    int bmp_size = LittleEndian.ToInt32 (bitmap, 2);
                    if (bitmap.Length - bmp_size > 0x36) // size of bmp header
                    {
                        if ('B' == bitmap[bmp_size] && 'M' == bitmap[bmp_size+1] &&
                            8 == bitmap[bmp_size+0x1c]) // 8bpp
                        {
                            uint alpha_width = LittleEndian.ToUInt32 (bitmap, bmp_size+0x12);
                            uint alpha_height = LittleEndian.ToUInt32 (bitmap, bmp_size+0x16);
                            if (info.Width == alpha_width && info.Height == alpha_height)
                                return BitmapWithAlphaChannel (meta, bitmap, bmp_size);
                        }
                    }
                }
                catch
                {
                    // ignore any errors occured during alpha-channel read attempt,
                    // fallback to a plain bitmap
                }
            }
            using (var bmp = new MemoryStream (bitmap))
                return base.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IafFormat.Write not implemented");
        }

        static ImageData BitmapWithAlphaChannel (ImageMetaData info, byte[] bitmap, int alpha_offset)
        {
            int src_pixel_size = info.BPP/8;
            int src_stride = (int)info.Width*src_pixel_size;
            int src_pixels = LittleEndian.ToInt32 (bitmap, 0x0A);
            int src_alpha  = alpha_offset + LittleEndian.ToInt32 (bitmap, alpha_offset+0x0A);
            var pixels = new byte[info.Width * info.Height * 4];
            int dst = 0;
            for (int y = (int)info.Height-1; y >= 0; --y)
            {
                int src = src_pixels + y*src_stride;
                int alpha = src_alpha + y*(int)info.Width;
                for (uint x = 0; x < info.Width; ++x)
                {
                    pixels[dst++] = bitmap[src];
                    pixels[dst++] = bitmap[src+1];
                    pixels[dst++] = bitmap[src+2];
                    pixels[dst++] = (byte)~bitmap[alpha++];
                    src += src_pixel_size;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        static byte[] ConvertCM (byte[] input, int width, int height, int bpp)
        {
            int src = LittleEndian.ToInt32 (input, 0x0a);
            int pixel_size = bpp / 8;
            var bitmap = new byte[input.Length];
            Buffer.BlockCopy (input, 0, bitmap, 0, src);
            int stride = width * pixel_size;
            int i = src;
            for (int p = 0; p < pixel_size; ++p)
            {
                for (int y = 0; y < height; ++y)
                {
                    int pixel = y * stride + p;
                    for (int x = 0; x < width; ++x)
                    {
                        bitmap[src+pixel] = input[i++];
                        pixel += pixel_size;
                    }
                }
            }
            return bitmap;
        }
    }

    internal class RleReader : IDataUnpacker, IDisposable
    {
        BinaryReader    m_input;
        byte[]          m_output;
        int             m_size;

        public byte[] Data { get { return m_output; } }

        public RleReader (Stream input, int input_length, int output_length)
        {
            m_input = new ArcView.Reader (input);
            m_output = new byte[output_length];
            m_size = input_length;
        }

        public void Unpack ()
        {
            int src = 0;
            int dst = 0;
            while (dst < m_output.Length && src < m_size)
            {
                byte ctl = m_input.ReadByte();
                ++src;
                if (0 == ctl)
                {
                    int count = m_input.ReadByte();
                    ++src;
                    count = Math.Min (count, m_output.Length - dst);
                    int read = m_input.Read (m_output, dst, count);
                    dst += count;
                    src += count;
                }
                else
                {
                    int count = ctl;
                    byte b = m_input.ReadByte();
                    ++src;
                    count = Math.Min (count, m_output.Length - dst);

                    for (int i = 0; i < count; i++)
                        m_output[dst++] = b;
                }
            }
        }

        #region IDisposable Members
        bool disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
            }
        }
        #endregion
    }
}
