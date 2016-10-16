//! \file       ImageGCP.cs
//! \date       Fri Feb 20 14:26:51 2015
//! \brief      GCP image format implementation.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Riddle
{
    internal class GcpMetaData : ImageMetaData
    {
        public int DataSize;
        public int PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GcpFormat : ImageFormat
    {
        public override string         Tag { get { return "GCP"; } }
        public override string Description { get { return "Riddle Soft compressed bitmap"; } }
        public override uint     Signature { get { return 0x31504d43u; } } // 'CMP1'

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GcpFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (12);
            int data_size = header.ToInt32 (4);
            int pack_size = header.ToInt32 (8);
            if (data_size < 54)
                return null;
            var reader = new CmpReader (stream.AsStream, pack_size, 0x22); // BMP header
            reader.Unpack();
            var bmp = reader.Data;
            if (bmp[0] != 'B' || bmp[1] != 'M')
                return null;
            int width = LittleEndian.ToInt32 (bmp, 0x12);
            int height = LittleEndian.ToInt32 (bmp, 0x16);
            int bpp = LittleEndian.ToInt16 (bmp, 0x1c);
            return new GcpMetaData
            {
                Width = (uint)width,
                Height = (uint)height,
                BPP = bpp,
                DataSize = data_size,
                PackedSize = pack_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GcpMetaData)info;
            stream.Position = 12;
            var reader = new CmpReader (stream.AsStream, meta.PackedSize, meta.DataSize);
            reader.Unpack();
            using (var bmp = new MemoryStream (reader.Data))
            {
                var decoder = new BmpBitmapDecoder (bmp,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                BitmapSource frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }
    }

    internal class CmpReader
    {
        Stream          m_input;
        byte[]          m_output;
        int             m_src_count = 0;
        int             m_src_total;

        public byte[] Data { get { return m_output; } }

        public CmpReader (Stream file, int src_size, int dst_size)
        {
            m_input = file;
            m_output = new byte[dst_size];
            m_src_total = src_size;
        }

        public void Unpack ()
        {
            int dst = 0;
            var shift = new byte[0x800];
            int edi = 0x7ef;
            for (int i = 0; i < edi; ++i)
                shift[i] = 0x20;
            while (dst < m_output.Length)
            {
                int bit = GetBits (1);
                if (-1 == bit)
                    break;
                if (1 == bit)
                {
                    int data = GetBits (8);
                    if (-1 == data)
                        break;
                    m_output[dst++] = (byte)data;
                    shift[edi++] = (byte)data;
                    edi &= 0x7ff;
                }
                else
                {
                    int offset = GetBits (11); // [esp+10]
                    if (-1 == offset)
                        break;
                    int count = GetBits (4);
                    if (-1 == count)
                        break;
                    count += 2;
                    for (int i = 0; i < count; ++i)
                    {
                        byte data = shift[(offset + i) & 0x7ff];
                        m_output[dst++] = data;
                        shift[edi++] = data;
                        edi &= 0x7ff;
                        if (m_output.Length == dst)
                            return;
                    }
                }
            }
        }

        int m_bits = 0;
        int m_cached_bits = 0;

        int GetBits (int count)
        {
            while (m_cached_bits < count)
            {
                if (m_src_count++ >= m_src_total)
                    return -1;
                int b = m_input.ReadByte();
                if (-1 == b)
                    return -1;
                m_bits = (m_bits << 8) | b;
                m_cached_bits += 8;
            }
            int mask = (1 << count) - 1;
            m_cached_bits -= count;
            return (m_bits >> m_cached_bits) & mask;
        }
    }
}
