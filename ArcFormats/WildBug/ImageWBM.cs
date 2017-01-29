//! \file       ImageWBM.cs
//! \date       Thu Jul 09 20:59:09 2015
//! \brief      Wild Bug's image format.
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
using GameRes.Utility;

namespace GameRes.Formats.WildBug
{
    internal class WbmMetaData : ImageMetaData
    {
        public int EntryCount;
        public int EntrySize;
        public byte[] Header;
    }

    internal class WpxSection
    {
        public int DataFormat;
        public int Offset;
        public int UnpackedSize;
        public int PackedSize;

        public static WpxSection Find (byte[] header, byte id, int count, int dir_size)
        {
            int ptr = 0;
            int n = 0;
            while (header[ptr] != id)
            {
                ptr += dir_size;
                if (ptr >= header.Length)
                    return null;
                if (++n >= count)
                    return null;
            }
            return new WpxSection {
                DataFormat = header[ptr+1],
                Offset = LittleEndian.ToInt32 (header, ptr+4),
                UnpackedSize = LittleEndian.ToInt32 (header, ptr+8),
                PackedSize = LittleEndian.ToInt32 (header, ptr+12),
            };
        }
    }

    [Export(typeof(ImageFormat))]
    public class WbmFormat : ImageFormat
    {
        public override string         Tag { get { return "WBM"; } }
        public override string Description { get { return "Wild Bug's image format"; } }
        public override uint     Signature { get { return 0x1A585057; } } // 'WPX'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x10);
            if (!header.AsciiEqual (4, "BMP"))
                return null;
            int count = header[0xE];
            int dir_size = header[0xF];
            if (1 != header[0xC] || 0 == count || 0 == dir_size)
                return null;
            var dir = stream.ReadBytes (count * dir_size);
            var section = WpxSection.Find (dir, 0x10, count, dir_size);
            if (null == section)
                return null;
            if (section.UnpackedSize < 0x10)
                return null;

            stream.Position = section.Offset;
            var data = stream.ReadBytes (section.UnpackedSize);
            if (data.Length != section.UnpackedSize)
                return null;

            return new WbmMetaData
            {
                Width  = LittleEndian.ToUInt16 (data, 4),
                Height = LittleEndian.ToUInt16 (data, 6),
                BPP    = data[0xC],
                EntryCount = count,
                EntrySize = dir_size,
                Header = dir,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (WbmMetaData)info;

            var section = WpxSection.Find (meta.Header, 0x11, meta.EntryCount, meta.EntrySize);
            if (null == section)
                throw new InvalidFormatException();

            PixelFormat format;
            int pixel_size;
            switch (meta.BPP)
            {
            case 24:
                format = PixelFormats.Bgr24;
                pixel_size = 3;
                break;
            case 32:
                format = PixelFormats.Bgr32;
                pixel_size = 4;
                break;
            case 16:
                format = PixelFormats.Bgr555;
                pixel_size = 2;
                break;
            case 8:
                format = PixelFormats.Indexed8;
                pixel_size = 1;
                break;
            default:
                throw new NotSupportedException ("Not supported WBM bitdepth");
            }
            int stride = ((int)meta.Width * pixel_size + 3) & -4;
            var reader = new WbmReader (stream, section);
            var pixels = reader.Unpack (stride, pixel_size, section.DataFormat);
            if (null == pixels)
                throw new InvalidFormatException();

            if (8 == meta.BPP)
            {
                section = WpxSection.Find (meta.Header, 0x12, meta.EntryCount, meta.EntrySize);
                if (null == section)
                    return ImageData.Create (info, PixelFormats.Gray8, null, pixels, stride);
                reader = new WbmReader (stream, section);
                var palette_data = reader.Unpack (48, 3, section.DataFormat);
                var palette = CreatePalette (palette_data);
                return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels, stride);
            }

            if (meta.BPP < 24)
                return ImageData.Create (info, format, null, pixels, stride);
            section = WpxSection.Find (meta.Header, 0x13, meta.EntryCount, meta.EntrySize);
            if (null == section)
                return ImageData.Create (info, format, null, pixels, stride);

            int alpha_stride = ((int)meta.Width + 3) & -4;
            byte[] alpha = null;
            try
            {
                reader = new WbmReader (stream, section);
                alpha = reader.Unpack (alpha_stride, 1, section.DataFormat);
            }
            catch { }
            if (null == alpha)
                return ImageData.Create (info, format, null, pixels, stride);

            byte[] alpha_image = new byte[4*meta.Width*meta.Height];
            int dst = 0;
            for (int y = 0; y < meta.Height; ++y)
            {
                int alpha_src = y * alpha_stride;
                int src = y * stride;
                for (int x = 0; x < meta.Width; ++x)
                {
                    alpha_image[dst++] = pixels[src];
                    alpha_image[dst++] = pixels[src+1];
                    alpha_image[dst++] = pixels[src+2];
                    alpha_image[dst++] = alpha[alpha_src+x];
                    src += pixel_size;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, alpha_image, (int)meta.Width*4);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("WbmFormat.Write not implemented");
        }

        static BitmapPalette CreatePalette (byte[] palette_data)
        {
            int colors = Math.Min (palette_data.Length/3, 0x100);
            var palette = new Color[0x100];
            for (int i = 0; i < colors; ++i)
            {
                int c = i * 3;
                palette[i] = Color.FromRgb (palette_data[c], palette_data[c+1], palette_data[c+2]);
            }
            return new BitmapPalette (palette);
        }
    }

    internal class WbmReader : WpxDecoder
    {
        public WbmReader (IBinaryStream input, WpxSection section) : base (input.AsStream, section)
        {
        }

        void GenerateOffsetTableV1 (int[] offset_table, int stride, int pixel_size)
        {
            offset_table[4] = pixel_size;
            offset_table[2] = 2 * pixel_size;
            offset_table[5] = 3 * pixel_size;
            if (5 * pixel_size < stride)
            {
                offset_table[6] = stride - pixel_size;
                offset_table[0] = stride;
                offset_table[7] = pixel_size + stride;
                offset_table[3] = 2 * pixel_size + stride;
                offset_table[1] = 2 * stride;
            }
            else
            {
                offset_table[6] = 4 * pixel_size;
                offset_table[0] = 5 * pixel_size;
                offset_table[7] = 6 * pixel_size;
                offset_table[3] = 7 * pixel_size;
                offset_table[1] = 8 * pixel_size;
            }
        }

        void GenerateOffsetTableV2 (int[] offset_table, int stride, int pixel_size)
        {
            offset_table[0] = pixel_size;
            offset_table[1] = 2 * pixel_size;
            offset_table[2] = 3 * pixel_size;
            if (5 * pixel_size < stride)
            {
                offset_table[3] = stride - pixel_size;
                offset_table[4] = stride;
                offset_table[5] = pixel_size + stride;
                offset_table[6] = 2 * pixel_size + stride;
                offset_table[7] = 2 * stride;
            }
            else
            {
                offset_table[3] = 4 * pixel_size;
                offset_table[4] = 5 * pixel_size;
                offset_table[5] = 6 * pixel_size;
                offset_table[6] = 7 * pixel_size;
                offset_table[7] = 8 * pixel_size;
            }
        }

        int m_version;
        int m_condition;

        public byte[] Unpack (int stride, int pixel_size, int flags) // sub_40919C
        {
            int[] offset_table = new int[8];
            GenerateOffsetTableV2 (offset_table, stride, pixel_size);
            for (m_version = 2; m_version >= 0; --m_version)
            {
                m_condition = m_version > 0 ? 1 : 0;
                try
                {
                    ResetInput();
                    if (0 == (flags & 0x80) && 0 != PackedSize)
                    {
                        byte[] ref_table = new byte[0x10000];
                        if (0 != (flags & 1))
                        {
                            if (0 != (flags & 8))
                            {
                                if (0 != (flags & 4))
                                    return UnpackVD (ref_table, offset_table, pixel_size);
                                else if (0 != (flags & 2))
                                    return UnpackVB (ref_table, offset_table, pixel_size);
                                else
                                    return UnpackV9 (offset_table, pixel_size);
                            }
                            else if (0 != (flags & 4))
                                return UnpackV5 (ref_table, offset_table, pixel_size);
                            else if (0 != (flags & 2))
                                return UnpackV3 (ref_table, offset_table, pixel_size);
                            else
                                return UnpackV1 (offset_table, pixel_size);
                        }
                        else if (0 != (flags & 4))
                            return UnpackV4 (ref_table, offset_table, pixel_size);
                        else if (0 != (flags & 2))
                            return UnpackV2 (ref_table, offset_table, pixel_size);
                        else
                            return UnpackV0 (offset_table, pixel_size);
                    }
                    else
                        return ReadUncompressed();
                }
                catch
                {
                    if (0 == m_version)
                        throw;
                }
                if (1 == m_version)
                    GenerateOffsetTableV1 (offset_table, stride, pixel_size);
            }
            return null;
        }

        byte[] UnpackVD (byte[] a4, int[] offset_table, int pixel_size) // 0x0F format
        {
            byte[] v47 = BuildTable(); //sub_46C26C();
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step + 0x80)
                return null;

            int v7 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v7 + 128;

            if (!FillRefTable (a4, pixel_size + v7))
                return null;

            int v45 = 16384;
            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v24 = 0;
                    int v25 = 0;
                    v45 &= ~0xff00;
                    v45 |= m_output[dst - pixel_size] << 8;
                    int v26 = 16384;
                    for (;;)
                    {
                        v24 = (v24 + 1) & 0xFF;
                        if (GetNextBit() != 0)
                            v25 |= v26;
                        if (a4[2 * v25] == v24)
                            break;
                        v26 >>= 1;
                        if (0 == v26)
                            return null;
                    }
                    v24 = a4[2 * v25 + 1];
                    byte v28 = v47[v45 + v24];
                    if (0 != v24)
                    {
                        Buffer.BlockCopy (v47, v45, v47, v45+1, v24);
                        v47[v45] = v28;
                    }
                    m_output[dst++] = v28;
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count;
                int src_offset;
                if (GetNextBit() != 0)
                {
                    int v37 = ReadNext();
                    count = 2;
                    src_offset = dst - 1 - v37;
                }
                else
                {
                    count = min_count;
                    int v36 = GetNextBit() << 2;
                    v36 |= GetNextBit() << 1;
                    v36 |= GetNextBit();
                    src_offset = dst - offset_table[v36];
                }
                if (0 == GetNextBit())
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackVB (byte[] a4, int[] offset_table, int pixel_size) // 0x0B format
        {
            byte[] v47 = BuildTable(); //sub_46C26C();
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step + 0x80)
                return null;

            int v7 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v7 + 128;

            if (!FillRefTable (a4, pixel_size + v7))
                return null;

            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v24 = 0;
                    int v25 = 0;
                    int v26 = 16384;
                    for (;;)
                    {
                        v24 = (v24 + 1) & 0xFF;
                        if (GetNextBit() != 0)
                            v25 |= v26;
                        if (a4[2 * v25] == v24)
                            break;
                        v26 >>= 1;
                        if (0 == v26)
                            return null;
                    }
                    m_output[dst++] = a4[2 * v25 + 1];
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count;
                int src_offset;
                if (GetNextBit() != 0)
                {
                    int v37 = ReadNext();
                    count = 2;
                    src_offset = dst - 1 - v37;
                }
                else
                {
                    count = min_count;
                    int v36 = GetNextBit() << 2;
                    v36 |= GetNextBit() << 1;
                    v36 |= GetNextBit();
                    src_offset = dst - offset_table[v36];
                }
                if (0 == GetNextBit())
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV9 (int[] offset_table, int pixel_size) // 0x09 format
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step)
                return null;

            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + (-pixel_size & 3);

            m_bits = m_buffer[m_current++];
            m_bit_count = 8;
            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    m_output[dst++] = ReadNext();
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count, src_offset;
                if (GetNextBit() != 0)
                {
                    src_offset = dst - 1 - ReadNext();
                    count = 2;
                }
                else
                {
                    count = min_count;
                    int v35 = GetNextBit();
                    v35 += v35 + GetNextBit();
                    v35 += v35 + GetNextBit();
                    src_offset = dst - offset_table[v35];
                }

                if (GetNextBit() == 0)
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }
   
        byte[] UnpackV5 (byte[] a4, int[] offset_table, int pixel_size) // 0x07 format
        {
            byte[] v46 = BuildTable();
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step + 0x80)
                return null;

            int v10 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v10 + 128;

            if (!FillRefTable (a4, pixel_size + v10))
                return null;

            int v43 = 16384;
            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v25 = 0;
                    int v26 = 0;
                    v43 &= ~0xff00;
                    v43 |= m_output[dst-pixel_size] << 8;
                    int v27 = 16384;
                    for (;;)
                    {
                        v25 = (v25 + 1) & 0xff;
                        if (GetNextBit() != 0)
                            v26 |= v27;
                        if (a4[2 * v26] == v25)
                            break;
                        v27 >>= 1;
                        if (0 == v27)
                            return null;
                    }
                    v25 = a4[2 * v26 + 1];
                    byte v29 = v46[v43 + v25];
                    if (0 != v25)
                    {
                        Buffer.BlockCopy (v46, v43, v46, v43+1, v25);
                        v46[v43] = v29;
                    }
                    m_output[dst++] = v29;
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count, src_offset;
                if (m_version > 1)
                {
                    if (GetNextBit() != 0)
                    {
                        int v35;
                        if (GetNextBit() != 0)
                        {
                            v35 = ReadNext();
                            count = 2;
                        }
                        else
                        {
                            v35  = ReadNext();
                            v35 |= ReadNext() << 8;
                            count = 3;
                        }
                        src_offset = dst - 1 - v35;
                    }
                    else
                    {
                        count = min_count;
                        int v35 = GetNextBit();
                        v35 += v35 + GetNextBit();
                        v35 += v35 + GetNextBit();
                        src_offset = dst - offset_table[v35];
                    }
                }
                else
                {
                    if (GetNextBit() != 0)
                    {
                        count = min_count;
                        int v32 = GetNextBit() << 2;
                        v32 |= GetNextBit() << 1;
                        v32 |= GetNextBit();
                        src_offset = dst - offset_table[v32];
                    }
                    else
                    {
                        byte v35 = ReadNext();
                        count = 2;
                        src_offset = dst - 1 - v35;
                    }
                }
                if (0 == GetNextBit())
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV4 (byte[] a4, int[] offset_table, int pixel_size) // 0x06 format
        {
            byte[] v48 = BuildTable();
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 4) & -4;
            if (m_available < step + 0x80)
                return null;

            int v10 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v10 + 128;

            if (!FillRefTable (a4, pixel_size + v10))
                return null;

            int v46 = 16384;
            while (remaining > 0)
            {
                int v28;
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v27 = 0;
                    v28 = 0;
                    v46 &= ~0xff00;
                    v46 |= m_output[dst - pixel_size] << 8;
                    int v29 = 16384;
                    for (;;)
                    {
                        v27 = (v27 + 1) & 0xff;
                        if (GetNextBit() != 0)
                            v28 |= v29;
                        if (a4[2 * v28] == v27)
                            break;
                        v29 >>= 1;
                        if (0 == v29)
                            return null;
                    }
                    v27 = a4[2 * v28 + 1];
                    byte v31 = v48[v46 + v27];
                    if (0 != v27)
                    {
                        Buffer.BlockCopy (v48, v46, v48, v46+1, v27);
                        v48[v46] = v31;
                    }
                    m_output[dst++] = v31;
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                v28 = GetNextBit() << 1;
                v28 |= GetNextBit();
                v28 <<= 1;
                v28 |= GetNextBit();
                int src_offset = dst - offset_table[v28];
                int count;
                if (GetNextBit() != 0)
                {
                    count = min_count;
                }
                else
                {
                    count = min_count + ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV3 (byte[] a4, int[] offset_table, int pixel_size) // 0x03 format
        {
            int min_count = 1 == pixel_size ? 2 : 1;

            m_available = FillBuffer();
            if (0 == m_available)
                return null;
            
            int step = (pixel_size + 3) & -4;
            if (m_available < step + 0x80)
                return null;

            int v9 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v9 + 128;

            if (!FillRefTable (a4, pixel_size + v9))
                return null;

            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v24 = 0;
                    int v25 = 0;
                    int v26 = 16384;
                    for (;;)
                    {
                        ++v24;
                        if (GetNextBit() != 0)
                            v25 |= v26;
                        if (a4[2 * v25] == v24)
                            break;
                        v26 >>= 1;
                        if (0 == v26)
                            return null;
                    }
                    m_output[dst++] = a4[2 * v25 + 1];
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count, src_offset;
                if (m_version > 1)
                {
                    if (GetNextBit() != 0)
                    {
                        if (GetNextBit() != 0)
                        {
                            count = 2;
                            src_offset = ReadNext();
                        }
                        else
                        {
                            count = 3;
                            src_offset  = ReadNext();
                            src_offset |= ReadNext() << 8;
                        }
                        src_offset = dst - 1 - src_offset;
                    }
                    else
                    {
                        count = min_count;
                        int v28 = GetNextBit();
                        v28 += v28 + GetNextBit();
                        v28 += v28 + GetNextBit();
                        src_offset = dst - offset_table[v28];
                    }
                }
                else
                {
                    if (GetNextBit() != 0)
                    {
                        count = min_count;
                        int v28 = GetNextBit() << 1;
                        v28 |= GetNextBit();
                        v28 <<= 1;
                        v28 |= GetNextBit();
                        src_offset = dst - offset_table[v28];
                    }
                    else
                    {
                        count = 2;
                        src_offset = dst - 1 - ReadNext();
                    }
                }
                if (GetNextBit() == 0)
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV2 (byte[] a4, int[] offset_table, int pixel_size) // 0x02 format
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step + 0x80)
                return null;

            int v9 = -pixel_size & 3;
            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + v9 + 128; // within m_buffer

            if (!FillRefTable (a4, pixel_size + v9))
                return null;

            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    int v20 = 0;
                    int v21 = 0;
                    v9 = 16384;
                    for (;;)
                    {
                        ++v20;
                        if (0 != GetNextBit())
                            v21 |= v9;
                        if (a4[2 * v21] == v20)
                            break;
                        v9 >>= 1;
                        if (0 == v9)
                            return null;
                    }
                    m_output[dst++] = a4[2 * v21 + 1];
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int v22 = GetNextBit() << 1;
                v22 |= GetNextBit();
                v22 <<= 1;
                v22 |= GetNextBit();
                int src_offset = dst - offset_table[v22];
                int count;
                if (0 != GetNextBit())
                {
                    count = min_count;
                }
                else
                {
                    count = min_count + ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV1 (int[] offset_table, int pixel_size) // 0x01 format
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step)
                return null;

            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + (-pixel_size & 3);

            m_bits = m_buffer[m_current++];
            m_bit_count = 8;
            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    m_output[dst++] = ReadNext();
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count, src_offset;
                if (m_version > 1)
                {
                    if (GetNextBit() != 0)
                    {
                        if (GetNextBit() != 0)
                        {
                            src_offset = ReadNext();
                            count = 2;
                        }
                        else
                        {
                            src_offset  = ReadNext();
                            src_offset |= ReadNext() << 8;
                            count = 3;
                        }
                        src_offset = dst - 1 - src_offset;
                    }
                    else
                    {
                        count = min_count;
                        int v20 = GetNextBit();
                        v20 += v20 + GetNextBit();
                        v20 += v20 + GetNextBit();
                        src_offset = dst - offset_table[v20];
                    }
                }
                else
                {
                    if (GetNextBit() != 0)
                    {
                        count = min_count;
                        int v14 = GetNextBit() << 2;
                        v14 |= GetNextBit() << 1;
                        v14 |= GetNextBit();
                        src_offset = dst - offset_table[v14];
                    }
                    else
                    {
                        count = 2;
                        src_offset = dst - 1 - ReadNext();
                    }
                }
                if (GetNextBit() == 0)
                {
                    count += ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }

        byte[] UnpackV0 (int[] offset_table, int pixel_size) // 0x00 format
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            m_available = FillBuffer();
            if (0 == m_available)
                return null;

            int step = (pixel_size + 3) & -4;
            if (m_available < step)
                return null;

            Buffer.BlockCopy (m_buffer, 0, m_output, 0, pixel_size);
            int dst = pixel_size;
            int remaining = m_output.Length - pixel_size;
            m_current = pixel_size + (-pixel_size & 3);

            m_bits = m_buffer[m_current++];
            m_bit_count = 8;
            while (remaining > 0)
            {
                while (0 == (GetNextBit() ^ m_condition))
                {
                    m_output[dst++] = ReadNext();
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int v14 = GetNextBit() << 1;
                v14 |= GetNextBit();
                v14 <<= 1;
                v14 |= GetNextBit();
                int src_offset = dst - offset_table[v14];
                int count;
                if (GetNextBit() != 0)
                {
                    count = min_count;
                }
                else
                {
                    count = min_count + ReadCount();
                }
                if (remaining < count)
                    return null;
                Binary.CopyOverlapped (m_output, src_offset, dst, count);
                dst += count;
                remaining -= count;
            }
            return m_output;
        }
    }

    internal class WpxDecoder
    {
        Stream              m_input;
        protected byte[]    m_output;
        int                 m_packed_size;
        int                 m_start_pos;

        public byte[] Data { get { return m_output; } }
        protected int PackedSize { get { return m_packed_size; } }

        protected WpxDecoder (Stream input, WpxSection section)
        {
            m_input = input;
            m_start_pos = section.Offset;
            m_output = new byte[section.UnpackedSize];
            m_packed_size = section.PackedSize;
        }

        protected byte[] ReadUncompressed ()
        {
            if (m_output.Length == m_input.Read (m_output, 0, m_output.Length))
                return m_output;
            else
                return null;
        }

        protected static byte[] BuildTable () // sub_4090E0
        {
            var table = new byte[0x100*0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                byte v2 = (byte)(-1 - i);
                for (int j = 0; j < 0x100; ++j)
                {
                    table[0x100*i + j] = v2--;
                }
            }
            return table;
        }

        protected bool FillRefTable (byte[] table, int src)
        {
            m_bits = m_buffer[m_current++];
            m_bit_count = 8;
            for (int n = 0; n < 0x100; )
            {
                byte v16 = m_buffer[src++];
                for (int half = 0; half < 2; ++half)
                {
                    byte v17 = (byte)(v16 & 0xF);
                    if (0 != v17)
                    {
                        int v18 = 0;
                        for (int i = v17; i != 0; --i)
                        {
                            if (0 == m_bit_count)
                            {
                                if (m_current >= m_available)
                                    return false;
                                m_bits = m_buffer[m_current++];
                                m_bit_count = 8;
                            }
                            int bit = m_bits >> 7;
                            m_bits <<= 1;
                            --m_bit_count;
                            v18 += v18 + bit;
                        }
                        if (15 != v17)
                            v18 <<= 15 - v17;
                        table[2 * v18] = v17;
                        table[2 * v18 + 1] = (byte)n;
                    }
                    ++n;
                    v16 >>= 4;
                }
            }
            return true;
        }

        protected byte[]  m_buffer = new byte[0x8000];
        protected int     m_current = 0;
        protected int     m_available = 0;

        protected byte ReadNext ()
        {
            if (m_current >= m_available)
            {
                m_available = FillBuffer();
                if (0 == m_available)
                    throw new InvalidFormatException ("Unexpected end of file");
                m_current = 0;
            }
            return m_buffer[m_current++];
        }

        protected int ReadCount ()
        {
            int n = 1;
            while (0 == GetNextBit())
            {
                ++n;
            }
            int count = 1;
            for (int i = 0; i < n; ++i)
            {
                count += count + GetNextBit();
            }
            return count - 1;
        }

        protected int m_input_remaining;

        protected void ResetInput ()
        {
            m_input.Position = m_start_pos;
            m_input_remaining = m_packed_size;
        }

        protected int FillBuffer () // sub_409B02
        {
            int read = 0;
            if (m_input_remaining > 0)
            {
                int size = Math.Min (m_input_remaining, 0x8000);
                m_input_remaining -= size;
                read = m_input.Read (m_buffer, 0, size);
            }
            return read;
        }

        protected byte m_bits;
        protected int  m_bit_count = 0;

        protected int GetNextBit ()
        {
            if (0 == m_bit_count)
            {
                m_bits = ReadNext();
                m_bit_count = 8;
            }
            int bit = m_bits >> 7;
            m_bits <<= 1;
            --m_bit_count;
            return bit;
        }
    }
}
