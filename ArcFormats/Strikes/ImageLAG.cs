//! \file       ImageLAG.cs
//! \date       2020 Mar 29
//! \brief      Strikes image format.
//
// Copyright (C) 2020 by morkt
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

namespace GameRes.Formats.Strikes
{
    internal class LagMetaData : ImageMetaData
    {
        public int  ScanLineSize;
        public bool HasPalette;
        public bool HasAlpha;
        public int  LastChunkSize;
        public int  ChunkCount;
    }

    [Export(typeof(ImageFormat))]
    public class LagFormat : ImageFormat
    {
        public override string         Tag { get { return "LAG"; } }
        public override string Description { get { return "Strikes image format"; } }
        public override uint     Signature { get { return 0x414C1001; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            int bpp = header[10] & 0x1F;
            if (!(bpp == 24 || bpp == 16 || bpp == 8))
                return null;
            return new LagMetaData
            {
                Width  = BigEndian.ToUInt16 (header, 4),
                Height = BigEndian.ToUInt16 (header, 6),
                BPP    = bpp,
                ScanLineSize  = BigEndian.ToUInt16 (header, 8),
                HasPalette    = (header[10] & 0x80) != 0,
                HasAlpha      = (header[10] & 0x20) != 0,
                LastChunkSize = BigEndian.ToInt32 (header, 0x14),
                ChunkCount    = BigEndian.ToUInt16 (header, 0x18),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new LagReader (file, (LagMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LagFormat.Write not implemented");
        }
    }

    internal class LagReader
    {
        IBinaryStream       m_input;
        byte[]              m_output;
        LagMetaData         m_info;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public LagReader (IBinaryStream input, LagMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[4 * m_info.Width * m_info.Height];

            switch (m_info.BPP)
            {
            case 24:
                if (m_info.HasAlpha)
                    Format = PixelFormats.Bgra32;
                else
                    Format = PixelFormats.Bgr24;
                break;

            default:
                throw new NotImplementedException ("Not supported LAG color depth.");
            }
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x20;
            if (m_info.HasPalette)
                Palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Bgr);

            var buffer = ReadChunks();
            using (var input = new BinMemoryStream (buffer))
            {
                int g_pos = m_info.iWidth;
                int b_pos = m_info.iWidth * 2;
                int a_pos = m_info.iWidth * 3;
                var scanline = new byte[m_info.ScanLineSize * 2];
                var unpack_buffer = new byte[scanline.Length];
                int dst = 0;
                for (int y = 0; y < m_info.iHeight; ++y)
                {
                    int packed_size = Binary.BigEndian (input.ReadInt24() << 8);
                    byte flags = input.ReadUInt8();
                    if (packed_size > scanline.Length)
                        break;
                    input.Read (scanline, 0, packed_size);
                    if ((flags & 4) != 0) // LZSS compression
                    {
                        packed_size = PckOpener.LzssUnpack (scanline, packed_size, unpack_buffer);
                        var swap = scanline;
                        scanline = unpack_buffer;
                        unpack_buffer = swap;
                    }
                    if ((flags & 2) != 0) // RLE compression
                    {
                        packed_size = RleUnpack (scanline, packed_size, unpack_buffer);
                        var swap = scanline;
                        scanline = unpack_buffer;
                        unpack_buffer = swap;
                    }
                    if ((flags & 1) != 0)
                    {
                        RestoreScanline (scanline, packed_size);
                    }
                    switch (m_info.BPP)
                    {
                    case 24:
                        {
                            int src_r = 0;
                            int src_g = g_pos;
                            int src_b = b_pos;
                            int src_a = a_pos;
                            for (int x = 0; x < m_info.iWidth; ++x)
                            {
                                m_output[dst++] = scanline[src_b++];
                                m_output[dst++] = scanline[src_g++];
                                m_output[dst++] = scanline[src_r++];
                                if (m_info.HasAlpha)
                                    m_output[dst++] = scanline[src_a++];
                            }
                            break;
                        }
                    default:
                        throw new NotImplementedException ("Not supported LAG color depth.");
                    }
                }
            }
            return m_output;
        }

        byte[] ReadChunks ()
        {
            var buffer = new byte[0x10000 * m_info.ChunkCount + m_info.LastChunkSize];
            int dst = 0;
            for (int i = 0; i <= m_info.ChunkCount; ++i)
            {
                int length = Binary.BigEndian (m_input.ReadInt32());
                bool is_compressed = length < 0;
                length &= 0x7FFFFFFF;
                if (is_compressed)
                {
                    int unpacked_size = Math.Min (0x10000, buffer.Length - dst);
                    using (var region = new StreamRegion (m_input.AsStream, m_input.Position, length, true))
                    using (var zinput = new ZLibStream (region, CompressionMode.Decompress, true))
                    {
                        dst += zinput.Read (buffer, dst, unpacked_size);
                    }
                }
                else
                {
                    dst += m_input.Read (buffer, dst, length);
                }
            }
            return buffer;
        }

        int RleUnpack (byte[] input, int in_length, byte[] output)
        {
            int dst = 0;
            int src = 0;
            while (src < in_length && dst < output.Length)
            {
                byte code = input[src++];
                if (src >= in_length)
                    break;
                int count = (code & 0x3F) + 1;
                if (dst + count > output.Length)
                    break;
                int val;
                int i;
                switch ((code & 0xC0) >> 6)
                {
                case 0:
                    for (i = 0; i < count; ++i)
                    {
                        int n = i & 3;
                        if (n == 0)
                            val = (input[src] & 0xC0) >> 6;
                        else if (n == 1)
                            val = (input[src] & 0x30) >> 4;
                        else if (n == 2)
                            val = (input[src] & 0xC) >> 2;
                        else
                            val = input[src++] & 3;
                        if ((val & 2) != 0)
                            val |= 0xFC;
                        output[dst++] = (byte)val;
                    }
                    if ((i & 3) != 0)
                        ++src;
                    break;

                case 1:
                    for (i = 0; i < count; ++i)
                    {
                        if ((i & 1) != 0)
                            val = input[src++] & 0xF;
                        else
                            val = (input[src] & 0xF0) >> 4;
                        if ((val & 8) != 0)
                            val |= 0xF0;
                        output[dst++] = (byte)val;
                    }
                    if ((i & 1) != 0)
                        ++src;
                    break;
                    
                case 2:
                    for (i = 0; i < count; ++i)
                    {
                        int n = i & 3;
                        if (n == 0)
                        {
                            val = (input[src] & 0xFC) >> 2;
                        }
                        else if (n == 1)
                        {
                            byte v = input[src++];
                            val = ((input[src] & 0xF0) >> 4) | (v & 3) << 4;
                        }
                        else if (n == 2)
                        {
                            byte v = input[src++];
                            val = ((input[src] & 0xC0) >> 6) | (v & 0xF) << 2;
                        }
                        else
                        {
                            val = input[src++] & 0x3F;
                        }
                        if ((val & 0x20) != 0)
                            val |= 0xC0;
                        output[dst++] = (byte)val;
                    }
                    if ((i & 3) != 0)
                        ++src;
                    break;

                case 3:
                    Buffer.BlockCopy (input, src, output, dst, count);
                    src += count;
                    dst += count;
                    break;
                }
            }
            return dst;

        }

        void RestoreScanline (byte[] input, int in_length)
        {
            for (int pos = 1; pos < in_length; ++pos)
            {
                input[pos] += input[pos-1];
            }
        }
    }
}
