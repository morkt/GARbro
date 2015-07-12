//! \file       ImageWBM.cs
//! \date       Thu Jul 09 20:59:09 2015
//! \brief      Wild Bug's image format.
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

    [Export(typeof(ImageFormat))]
    public class WbmFormat : ImageFormat
    {
        public override string         Tag { get { return "WBM"; } }
        public override string Description { get { return "Wild Bug's image format"; } }
        public override uint     Signature { get { return 0x1A585057; } } // 'WPX'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            byte[] header = new byte[0x10];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int count = header[0xE];
            int dir_size = header[0xF];
            if (1 != header[0xC] || 0 == count || 0 == dir_size)
                return null;
            header = new byte[count * dir_size];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int? ptr = GetDataBlock (header, 0x10, count, dir_size);
            if (null == ptr)
                return null;
            int data_offset = LittleEndian.ToInt32 (header, ptr.Value+4);
            int size = LittleEndian.ToInt32 (header, ptr.Value+8);
            if (size < 0x10)
                return null;

            stream.Seek (data_offset, SeekOrigin.Begin);
            byte[] data = new byte[size];
            if (size != stream.Read (data, 0, size))
                return null;

            return new WbmMetaData
            {
                Width  = LittleEndian.ToUInt16 (data, 4),
                Height = LittleEndian.ToUInt16 (data, 6),
                BPP    = data[0xC],
                EntryCount = count,
                EntrySize = dir_size,
                Header = header,
            };
        }

        private int? GetDataBlock (byte[] header, byte id, int count, int dir_size)
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
            return ptr;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as WbmMetaData;
            if (null == meta)
                throw new ArgumentException ("WbmFormat.Read should be supplied with WbmMetaData", "info");

            int? ptr = GetDataBlock (meta.Header, 0x11, meta.EntryCount, meta.EntrySize);
            if (null == ptr)
                throw new InvalidFormatException();
            int data_format = meta.Header[ptr.Value+1];
            int data_offset = LittleEndian.ToInt32 (meta.Header, ptr.Value+4);
            int unpacked_size = LittleEndian.ToInt32 (meta.Header, ptr.Value+8);
            int packed_size   = LittleEndian.ToInt32 (meta.Header, ptr.Value+12);

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
            default:
                throw new NotSupportedException ("Not supported WBM bitdepth");
            }
            int stride = ((int)meta.Width * pixel_size + 3) & -4;
            var reader = new WbmReader (stream, data_offset, unpacked_size, packed_size);
            var pixels = reader.Unpack (stride, pixel_size, meta.Header[ptr.Value+1]);
            if (null == pixels)
                throw new InvalidFormatException();

            return ImageData.Create (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("WbmFormat.Write not implemented");
        }
    }

    internal class WbmReader
    {
        Stream      m_input;
        byte[]      m_output;
        int         m_packed_size;

        public byte[] Data { get { return m_output; } }

        public WbmReader (Stream input, int offset, int unpacked_size, int packed_size)
        {
            m_input = input;
            m_input.Position = offset;
            m_output = new byte[unpacked_size];
            m_packed_size = packed_size;
        }

        public byte[] Unpack (int stride, int pixel_size, int flags) // sub_40919C
        {
            int[] offset_table = new int[8];
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
            if (0 == (flags & 0x80) && 0 != m_packed_size)
            {
                byte[] ref_table = new byte[0x10000];
                if (0 != (flags & 1))
                {
                    if (0 != (flags & 4))
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
            else if (m_output.Length == m_input.Read (m_output, 0, m_output.Length))
                return m_output;

            return null;
        }
   
        byte[] UnpackV5 (byte[] a4, int[] offset_table, int pixel_size) // 0x07 format
//        int sub_409AF4 (void *a1, FILE *stream, void *ptr, void *a4, unsigned int m_packed_size, int *offset_table, int unpacked_size, unsigned int pixel_size)
        {
            byte[] v46 = BuildTable();
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;
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
                while (0 == GetNextBit())
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
                int src_offset;
                int count;
                if (GetNextBit() != 0)
                {
                    count = min_count;
                    int v32 = GetNextBit() << 1;
                    v32 |= GetNextBit();
                    v32 <<= 1;
                    v32 |= GetNextBit();
                    src_offset = dst - offset_table[v32];
                }
                else
                {
                    byte v35 = ReadNext();
                    count = 2;
                    src_offset = dst - 1 - v35;
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
//        signed int __cdecl sub_40B044(void *a1, FILE *stream, void *ptr, void *a4, unsigned int packed_size, int *offset_table, int unpacked_size, unsigned int pixel_size)
        {
            byte[] v48 = BuildTable();
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;
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
                while (0 == GetNextBit())
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
//        int sub_409F70(void *a1, FILE *stream, void *ptr, void *a4, unsigned int packed_size, int *offset_table, int unpacked_size, unsigned int pixel_size)
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;

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
                while (GetNextBit() == 0)
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
//        int sub_40B458(void *a1, FILE *stream, unsigned __int8 *ptr, void *a4, unsigned int packed_size, int *offset_table, int unpacked_size, unsigned int a8)
        {
            byte[] v48 = BuildTable();
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;
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
                while (0 == GetNextBit())
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
        //int sub_40A3C4(void *a1, FILE *stream, const void *ptr, unsigned int packed_size, int *offset_table, int unpacked_size, unsigned int pixel_size)
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;
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
                while (0 == GetNextBit())
                {
                    m_output[dst++] = ReadNext();
                    --remaining;
                    if (0 == remaining)
                        return m_output;
                }
                int count, src_offset;
                if (GetNextBit() != 0)
                {
                    count = min_count;
                    int v14 = GetNextBit() << 1;
                    v14 |= GetNextBit();
                    v14 <<= 1;
                    v14 |= GetNextBit();
                    src_offset = dst - offset_table[v14];
                }
                else
                {
                    count = 2;
                    src_offset = dst - 1 - ReadNext();
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
//        int sub_40B83C(void *a1, FILE *stream, const void *ptr, unsigned int packed_size, int *a5, int unpacked_size, unsigned int pixel_size)
        {
            int min_count = 1 == pixel_size ? 2 : 1;
            if (0 == m_packed_size)
                return null;
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
                while (0 == GetNextBit())
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

        int ReadCount ()
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

        static byte[] BuildTable () // sub_4090E0
        {
            var table = new byte[0x100*0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                byte v2 = (byte)(-1 - i);
                for (int j = 0; j < 0x100; ++j)
                {
                    table[0x100*i + j] = v2--; // *(&byte_41B0C0[256 * i] + j) = v2--;
                }
            }
            return table;
        }

        byte[]  m_buffer = new byte[0x8000];
        int     m_current = 0;
        int     m_available = 0;

        byte ReadNext ()
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

        int FillBuffer () // sub_409B02
        {
            int read = 0;
            if (m_packed_size > 0)
            {
                int size = Math.Min (m_packed_size, 0x8000);
                m_packed_size -= size;
                read = m_input.Read (m_buffer, 0, size);
            }
            return read;
        }

        byte m_bits;
        int  m_bit_count = 0;

        int GetNextBit ()
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

        bool FillRefTable (byte[] table, int src)
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
    }
}
