//! \file       ImageSPD.cs
//! \date       Thu Oct 08 16:25:55 2015
//! \brief      TopCat compressed image.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.TopCat
{
    internal class SpdMetaData : ImageMetaData
    {
        public Compression  Method;
        public uint         UnpackedSize;
        public bool         IsSpd8;
    }

    internal enum Compression
    {
        LzRle       = 0,
        Lz          = 1,
        LzRleAlpha  = 2,
        LzRle2      = 0x100,
        Spdc        = 0x101,
        LzRleAlpha2 = 0x102,
        Jpeg        = 0x103,
    }

    [Export(typeof(ImageFormat))]
    public class SpdFormat : ImageFormat
    {
        public override string         Tag { get { return "SPD"; } }
        public override string Description { get { return "TopCat compressed image format"; } }
        public override uint     Signature { get { return 0x43445053; } } // 'SPDC'

        public SpdFormat ()
        {
            Signatures = new uint[] { 0x43445053, 0x38445053 }; // 'SPD8'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14).ToArray();
            unsafe
            {
                fixed (byte* raw = header)
                {
                    uint* dw = (uint*)raw;
                    dw[3] -= (dw[4] >> 2) & 0xF731;
                    dw[2] -= (dw[4] << 2) & 0x137F;
                    dw[1] -= (dw[4] << 4) & 0xFFFF;
                    return new SpdMetaData
                    {
                        Width  = dw[2],
                        Height = dw[3],
                        BPP    = (int)(dw[1] >> 16),
                        Method = (Compression)(dw[1] & 0xFFFF),
                        UnpackedSize = dw[4],
                        IsSpd8 = header[3] == '8',
                    };
                }
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SpdMetaData)info;
            if (Compression.Jpeg == meta.Method)
                return ReadJpeg (stream.AsStream, meta);

            using (var reader = new SpdReader (stream.AsStream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SpdFormat.Write not implemented");
        }

        private ImageData ReadJpeg (Stream file, SpdMetaData info)
        {
            file.Position = 0x18;
            var header = new byte[0x3C];
            if (header.Length != file.Read (header, 0, header.Length))
                throw new EndOfStreamException();
            unsafe
            {
                fixed (byte* raw = header)
                {
                    uint* dw = (uint*)raw;
                    for (int i = 0; i < 0xF; ++i)
                        dw[i] += 0xA8961EF1;
                }
            }
            using (var rest = new StreamRegion (file, file.Position, true))
            using (var jpeg = new PrefixStream (header, rest))
            {
                var decoder = new JpegBitmapDecoder (jpeg,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }
    }

    internal sealed class SpdReader : IDisposable
    {
        Stream          m_input;
        byte[]          m_output;
        SpdMetaData     m_info;

        public PixelFormat Format { get; private set; }
        public byte[]        Data { get { return m_output; } }

        public SpdReader (Stream input, SpdMetaData info)
        {
            m_input = input;
            m_output = new byte[info.UnpackedSize];
            m_info = info;
            if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                Format = PixelFormats.Bgra32;
            else
                throw new NotSupportedException ("Not supported SPD image bitdepth");
        }

        public void Unpack ()
        {
            m_input.Position = 0x14;
            switch (m_info.Method)
            {
            case Compression.Spdc:
                UnpackSpdc();
                break;
            case Compression.Lz:
                UnpackLz();
                break;
            case Compression.LzRle:
            case Compression.LzRle2:
            case Compression.LzRleAlpha:
            case Compression.LzRleAlpha2:
                {
                    UnpackLz();
                    var rgb = new byte[m_info.Height * m_info.Width * 4];
                    if (Compression.LzRle == m_info.Method || Compression.LzRle2 == m_info.Method)
                        UnpackRle (rgb);
                    else if (m_info.IsSpd8)
                        UnpackSpd8Alpha (rgb);
                    else
                        UnpackRleAlpha (rgb);
                    m_output = rgb;
                    Format = PixelFormats.Bgra32;
                    break;
                }
            default:
                throw new NotImplementedException ("SPD compression method not implemented");
            }
        }

        void UnpackLz ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    break;
                for (int bit = 1; bit != 0x100 && dst < m_output.Length; bit <<= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        int b = m_input.ReadByte();
                        if (-1 == b)
                            return;
                        m_output[dst++] = (byte)b;
                    }
                    else
                    {
                        int lo = m_input.ReadByte();
                        if (-1 == lo)
                            return;
                        int hi = m_input.ReadByte();
                        if (-1 == hi)
                            return;
                        int src = lo >> 4 | hi << 4;
                        int count = Math.Min (3 + (lo & 0xF), m_output.Length - dst);
                        Binary.CopyOverlapped (m_output, dst-src, dst, count);
                        dst += count;
                    }
                }
            }
        }

        void UnpackRle (byte[] rgb)
        {
            int rgb_src = LittleEndian.ToInt32 (m_output, 0);
            bool skip = 0 == LittleEndian.ToInt32 (m_output, 8);
            int ctl_src = 12;
            int dst = 0;
            while (dst < rgb.Length)
            {
                int n = LittleEndian.ToInt32 (m_output, ctl_src);
                ctl_src += 4;
                if (skip)
                {
                    dst += n * 4;
                }
                else
                {
                    for (; n != 0; --n)
                    {
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = 0xFF;
                    }
                }
                skip = !skip;
            }
        }

        void UnpackRleAlpha (byte[] rgb)
        {
            int rgb_src = LittleEndian.ToInt32 (m_output, 0);
            int ctl_src = 8;
            int dst = 0;
            while (dst < rgb.Length)
            {
                int count = LittleEndian.ToUInt16 (m_output, ctl_src);
                ctl_src += 2;

                int control = count >> 14;
                count &= 0x3FFF;
                if (0 == control)
                {
                    dst += 4 * count;
                }
                else
                {
                    for (; count != 0; --count)
                    {
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        if (1 == control)
                            rgb[dst++] = 0xFF;
                        else
                            rgb[dst++] = m_output[ctl_src++];
                    }
                }
            }
        }

        void UnpackSpd8Alpha (byte[] rgb)
        {
            int rgb_src = LittleEndian.ToInt32 (m_output, 0);
            int ctl_src = 8;
            int dst = 0;
            while (dst < rgb.Length)
            {
                int control = m_output[ctl_src++];
                if (0 == control)
                {
                    int count = m_output[ctl_src++] + 1;
                    dst += 4 * count;
                }
                else if (1 == control)
                {
                    int count = m_output[ctl_src++] + 1;
                    for (int i = 0; i < count; ++i)
                    {
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = m_output[rgb_src++];
                        rgb[dst++] = 0xFF;
                    }
                }
                else
                {
                    rgb[dst++] = m_output[rgb_src++];
                    rgb[dst++] = m_output[rgb_src++];
                    rgb[dst++] = m_output[rgb_src++];
                    rgb[dst++] = (byte)~(control - 1);
                }
            }
        }

        void UnpackSpdc ()
        {
            int pixel_size = m_info.BPP / 8;
            int stride = pixel_size * (int)m_info.Width;
            int[] offset_table = new int[28];
            int i = 0;
            while (i < 16)
                offset_table[i++] = -pixel_size;
            while (i < 24)
                offset_table[i++] = -stride;
            while (i < 26)
                offset_table[i++] = -stride - pixel_size;
            while (i < 28)
                offset_table[i++] = -stride + pixel_size;

            int dst = 0;
            for (i = 0; i < pixel_size; ++i)
                m_output[dst++] = (byte)GetBits (8);

            while (dst < m_output.Length)
            {
                int x = GetBits (5, true);
                if (x > 0x1B)
                {
                    GetBits (3);
                    m_output[dst+2] = (byte)GetBits (8);
                    m_output[dst+1] = (byte)GetBits (8);
                    m_output[dst]   = (byte)GetBits (8);
                }
                else
                {
                    int src = dst + offset_table[x];
                    m_output[dst]   = m_output[src];
                    m_output[dst+1] = m_output[src+1];
                    m_output[dst+2] = m_output[src+2];

                    GetBits (DiffPrefixTable[x] >> 1);
                    if (0 != (DiffPrefixTable[x] & 1))
                    {
                        int i1 = GetBits (8, true);
                        int i2 = GetBits (8 + DiffLengthsTable[i1], true) & 0xFF;
                        int i3 = GetBits (8 + DiffLengthsTable[i1] + DiffLengthsTable[i2], true) & 0xFF;

                        m_output[dst]   += DiffTable[i1];
                        m_output[dst+1] += (byte)(DiffTable[i1] + DiffTable[i2]);
                        m_output[dst+2] += (byte)(DiffTable[i1] + DiffTable[i3]);

                        GetBits (DiffLengthsTable[i1] + DiffLengthsTable[i2] + DiffLengthsTable[i3]);
                    }
                }
                dst += pixel_size;
            }
        }

        int m_bits = 0;
        int m_cached_bits = 0;

        // FIXME: add 'peek' feature to MsbBitStream class
        int GetBits (int count, bool peek = false)
        {
            while (m_cached_bits < count)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    return 0;
                m_bits = (m_bits << 8) | b;
                m_cached_bits += 8;
            }
            int mask = (1 << count) - 1;
            int left_bits = m_cached_bits - count;
            if (!peek)
                m_cached_bits = left_bits;
            return (m_bits >> left_bits) & mask;
        }

        static readonly byte[] DiffPrefixTable = {
            0x4, 0x4, 0x4, 0x4, 0x4, 0x4, 0x4, 0x4,
            0x5, 0x5, 0x5, 0x5, 0x5, 0x5, 0x5, 0x5,
            0x6, 0x6, 0x6, 0x6, 0x7, 0x7, 0x7, 0x7,
            0xA, 0xB, 0xA, 0xB,
        };
        static readonly byte[] DiffLengthsTable = {
            8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
            7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        };
        static readonly byte[] DiffTable = {
            0x10, 0x0F, 0x0E, 0x0D, 0x0C, 0x0B, 0x0A, 0x09, 0xF7, 0xF6, 0xF5, 0xF4, 0xF3, 0xF2, 0xF1, 0xF0,
            0x08, 0x08, 0x07, 0x07, 0x06, 0x06, 0x05, 0x05, 0xFB, 0xFB, 0xFA, 0xFA, 0xF9, 0xF9, 0xF8, 0xF8,
            0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03,
            0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFD, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC, 0xFC,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
            0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE,
        };

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
        #endregion
    }
}
