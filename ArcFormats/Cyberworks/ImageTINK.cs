//! \file       ImageTINK.cs
//! \date       Fri Jun 17 18:49:04 2016
//! \brief      Tinker Bell encrypted image file.
//
// Copyright (C) 2016-2017 by morkt
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
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Cyberworks
{
    public enum AImageHeader
    {
        Flags           = 0,
        Field1          = 1,
        Field2          = 2,
        Height          = 3,
        Width           = 4,
        UnpackedSize    = 5,
        AlphaSize       = 6,
        BitsSize        = 7,
        Ignored         = Field1,
    }

    internal sealed class AImageReader : IImageDecoder
    {
        readonly ImageMetaData m_info = new ImageMetaData();

        IBinaryStream   m_input;
        byte[]          m_output;
        AImageScheme    m_scheme;
        ImageData       m_image;
        int[]           m_header;
        int             m_type;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public byte[]          Baseline { get; set; }
        public int                 Type { get { return m_type; } }

        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    Unpack();
                    int stride = (int)Info.Width * Info.BPP / 8;
                    if (m_scheme.Flipped)
                        m_image = ImageData.CreateFlipped (Info, GetPixelFormat(), null, Data, stride);
                    else
                        m_image = ImageData.Create (Info, GetPixelFormat(), null, Data, stride);
                }
                return m_image;
            }
        }

        public byte[] Data { get { return m_output; } }

        public AImageReader (IBinaryStream input, AImageScheme scheme, int type = 'a')
        {
            m_input = input;
            m_scheme = scheme;
            m_type = type;
        }

        internal int[] ReadHeader ()
        {
            if (m_header != null)
                return m_header;
            int header_length = Math.Max (8, m_scheme.HeaderOrder.Length);
            m_header = new int[header_length];
            for (int i = 0; i < m_scheme.HeaderOrder.Length; ++i)
            {
                int b = GetInt();
                m_header[m_scheme.HeaderOrder[i]] = b;
            }
            Info.Width  = (uint)m_header[4];
            Info.Height = (uint)m_header[3];
            return m_header;
        }

        /// <summary>
        /// Search archive <paramref name="arc"/> for baseline image.
        /// </summary>
        internal void ReadBaseline (BellArchive arc, Entry entry)
        {
            var header = ReadHeader();
            if (!((header[0] & 1) == 1 && 'd' == this.Type
                  || header[0] == 1 && 'a' == this.Type))
                return;
            var scheme = arc.Scheme;
            var dir = (List<Entry>)arc.Dir;
            int i = dir.IndexOf (entry);
            while (--i >= 0 && "image" == dir[i].Type)
            {
                using (var input = arc.OpenEntry (dir[i]))
                {
                    int type = input.ReadByte();
                    if ('d' == type)
                        continue;
                    if ('a' == type)
                    {
                        int id = input.ReadByte();
                        if (id != scheme.Value2)
                            break;
                        using (var bin = new BinaryStream (input, dir[i].Name))
                        using (var base_image = new AImageReader (bin, scheme))
                        {
                            var base_header = base_image.ReadHeader();
                            if (1 == base_header[0])
                                continue;
                            // check if image width/height are the same
                            if (base_header[3] == header[3] && base_header[4] == header[4])
                            {
                                base_image.Unpack();
                                Baseline = base_image.Data;
                            }
                        }
                    }
                    else if ('b' == type || 'c' == type)
                    {
                        var size_buf = new byte[4];
                        input.Read (size_buf, 0 , 4);
                        var decoder = new PngBitmapDecoder (input, BitmapCreateOptions.None,
                                                            BitmapCacheOption.OnLoad);
                        BitmapSource frame = decoder.Frames[0];
                        Info.Width = (uint)frame.PixelWidth;
                        Info.Height = (uint)frame.PixelHeight;
                        if (frame.Format.BitsPerPixel != 32)
                            frame = new FormatConvertedBitmap (frame, PixelFormats.Bgra32, null, 0);
                        int stride = frame.PixelWidth * 4;
                        var pixels = new byte[stride * frame.PixelHeight];
                        frame.CopyPixels (pixels, stride, 0);
                        Baseline = pixels;
                    }
                    break;
                }
            }
        }

        public void Unpack ()
        {
            var header = ReadHeader();
            if (0 == Info.Width || Info.Width >= 0x8000 || 0 == Info.Height || Info.Height >= 0x8000)
                throw new InvalidFormatException();
            int flags     = header[0];
            int unpacked_size = header[5];
            int bits_size = header[7];
            if (unpacked_size <= 0)
            {
                if (0 == unpacked_size && 0 == header[6]
                    && (1 == (flags & 1) && 'd' == m_type && Baseline != null))
                {
                    UnpackV6NoAlpha (bits_size);
                    return;
                }
                throw new InvalidFormatException();
            }
            int data_offset = bits_size * 2;
            if (0 == flags)
                CopyV0 (unpacked_size);
            else if (2 == (flags & 6))
                UnpackV2 (bits_size, data_offset);
            else if (6 == (flags & 6))
            {
                if (0 == bits_size)
                    CopyV6 (unpacked_size, header[6]);
                else if (1 == (flags & 1) && 'd' == m_type && Baseline != null)
                    UnpackV6d (bits_size, bits_size + header[6]);
                else
                    UnpackV6 (bits_size, data_offset, data_offset + header[6]);
            }
            else if (0 == bits_size)
                CopyV0 (unpacked_size);
            else
                UnpackV1 (bits_size, unpacked_size);
        }

        void CopyV0 (int data_size)
        {
            int plane_size = (int)Info.Width * (int)Info.Height;
            if (plane_size == data_size)
            {
                Info.BPP = 8;
                m_output = m_input.ReadBytes (data_size);
            }
            else if (3 * plane_size == data_size)
            {
                Info.BPP = 24;
                m_output = m_input.ReadBytes (data_size);
            }
            else if (4 * plane_size == data_size)
            {
                Info.BPP = 32;
                m_output = m_input.ReadBytes (data_size);
            }
            else
            {
                Info.BPP = 24;
                int dst_stride = (int)Info.Width * 3;
                int src_stride = (dst_stride + 3) & ~3;
                if (src_stride * (int)Info.Height != data_size)
                    throw new InvalidFormatException();
                m_output = new byte[dst_stride * (int)Info.Height];
                var gap = new byte[src_stride-dst_stride];
                int dst = 0;
                for (uint y = 0; y < Info.Height; ++y)
                {
                    m_input.Read (m_output, dst, dst_stride);
                    m_input.Read (gap, 0, gap.Length);
                    dst += dst_stride;
                }
            }
        }

        void UnpackV1 (int alpha_size, int rgb_size)
        {
            var alpha_map = m_input.ReadBytes (alpha_size);
            if (alpha_map.Length != alpha_size)
                throw new InvalidFormatException();

            int plane_size = (int)Info.Width * (int)Info.Height;
            if (Baseline != null)
            {
                Info.BPP = 24;
                m_output = Baseline;
            }
            else
            {
                Info.BPP = 32;
                m_output = new byte[plane_size * 4];
            }
            int pixel_size = Info.BPP / 8;
            int bit = 1;
            int bit_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                byte alpha = 0;
                if ((bit & alpha_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                    alpha = 0xFF;
                }
                if (4 == pixel_size)
                    m_output[dst+3] = alpha;
                dst += pixel_size;
                if (0x80 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
                else
                    bit <<= 1;
            }
        }

        void UnpackV2 (int offset1, int rgb_offset)
        {
            Info.BPP = 24;
            var rgb_map = m_input.ReadBytes (offset1);
            var alpha_map = m_input.ReadBytes (rgb_offset-offset1);
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 3];

            int bit = 1;
            int bit_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                if ((bit & alpha_map[bit_src]) == 0 && (bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                }
                dst += 3;
                if (0x80 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
                else
                    bit <<= 1;
            }
        }

        void CopyV6 (int alpha_size, int rgb_size)
        {
            Info.BPP = 32;
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 4];
            int stride = ((int)Info.Width * 3 + 3) & ~3;
            var line = new byte[stride];
            int dst = 3;
            for (uint y = 0; y < Info.Height; ++y)
            {
                m_input.Read (line, 0, stride);
                int src = 0;
                for (uint x = 0; x < Info.Width; ++x)
                {
                    m_output[dst] = line[src];
                    dst += 4;
                    src += 3;
                }
            }
            dst = 0;
            for (uint y = 0; y < Info.Height; ++y)
            {
                m_input.Read (line, 0, stride);
                int src = 0;
                for (uint x = 0; x < Info.Width; ++x)
                {
                    m_output[dst  ] = line[src++];
                    m_output[dst+1] = line[src++];
                    m_output[dst+2] = line[src++];
                    dst += 4;
                }
            }
        }

        void UnpackV6 (int offset1, int alpha_offset, int rgb_offset)
        {
            Info.BPP = 32;
            var rgb_map = m_input.ReadBytes (offset1);
            var alpha_map = m_input.ReadBytes (alpha_offset - offset1);
            var alpha = m_input.ReadBytes (rgb_offset - alpha_offset);
            int plane_size = (int)Info.Width * (int)Info.Height;
            m_output = new byte[plane_size * 4];

            int bit = 1;
            int bit_src = 0;
            int alpha_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                bool has_alpha = (bit & alpha_map[bit_src]) != 0;
                if (has_alpha || (bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                    if (has_alpha && alpha_src < alpha.Length)
                    {
                        m_output[dst+3] = alpha[alpha_src];
                        alpha_src += 3;
                    }
                    else
                        m_output[dst+3] = 0xFF;
                }
                dst += 4;
                if (0x80 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
                else
                    bit <<= 1;
            }
        }

        void UnpackV6d (int bits_size, int rgb_offset)
        {
            Info.BPP = 32;
            var rgb_map = m_input.ReadBytes (bits_size);
            var alpha = m_input.ReadBytes (rgb_offset - bits_size);
            int plane_size = Math.Min (Baseline.Length, bits_size*8);
            m_output = Baseline;
            int bit = 1;
            int bit_src = 0;
            int alpha_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                if ((bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                    m_output[dst+3] = alpha[alpha_src++];
                }
                dst += 4;
                bit <<= 1;
                if (0x100 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
            }
        }

        void UnpackV6NoAlpha (int bits_size)
        {
            Info.BPP = 32;
            var rgb_map = m_input.ReadBytes (bits_size);
            int plane_size = Math.Min (Baseline.Length, bits_size*8);
            m_output = Baseline;
            int bit = 1;
            int bit_src = 0;
            int dst = 0;
            for (int i = 0; i < plane_size; ++i)
            {
                if ((bit & rgb_map[bit_src]) != 0)
                {
                    m_input.Read (m_output, dst, 3);
                }
                dst += 4;
                bit <<= 1;
                if (0x100 == bit)
                {
                    ++bit_src;
                    bit = 1;
                }
            }
        }

        int GetInt ()
        {
            byte a = m_input.ReadUInt8();
            if (a == m_scheme.Value3)
                a = 0;
            int d = 0;
            int c = 0;
            for (;;)
            {
                byte a1 = m_input.ReadUInt8();
                if (a1 == m_scheme.Value2)
                    break;
                if (a1 != m_scheme.Value1)
                {
                    c = (a1 == m_scheme.Value3) ? 0 : a1;
                }
                else
                {
                    ++d;
                }
            }
            return a + (c + d * m_scheme.Value1) * m_scheme.Value1;
        }

        PixelFormat GetPixelFormat ()
        {
            switch (Info.BPP)
            {
            case 8:  return PixelFormats.Gray8;
            case 24: return PixelFormats.Bgr24;
            case 32: return PixelFormats.Bgra32;
            default: throw new InvalidFormatException();
            }
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
