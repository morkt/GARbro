//! \file       ImageEDT.cs
//! \date       Sun Feb 15 10:13:07 2015
//! \brief      Active Soft image format implementation.
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

namespace GameRes.Formats.AdPack
{
    internal class EdtMetaData : ImageMetaData
    {
        public uint CompSize;
        public uint ExtraSize;
    }

    internal class Ed8MetaData : ImageMetaData
    {
        public int  PaletteSize;
        public uint CompSize;
    }

    internal class BitReader
    {
        int m_bits = 1;

        protected void ResetBits ()
        {
            m_bits = 1;
        }

        protected int NextBit (Stream input)
        {
            if (1 == m_bits)
            {
                m_bits = input.ReadByte();
                if (-1 == m_bits)
                    throw new InvalidFormatException ("Unexpected end of input");
                m_bits |= 0x100;
            }
            int bit = m_bits & 1;
            m_bits >>= 1;
            return bit;
        }

        protected int ReadBits (int data, int count, Stream input)
        {
            while (count > 0)
            {
                data = (data << 1) | NextBit (input);
                --count;
            }
            return data;
        }

        protected int CountBits (Stream input)
        {
            int bit = 1, count = 0;
            while (count < 0x20 && 1 == bit)
            {
                ++count;
                bit = NextBit (input);
            }
            if (--count != 0)
                return ReadBits (1, count, input);
            else 
                return 1;
        }
    }

    [Export(typeof(ImageFormat))]
    public class EdtFormat : ImageFormat
    {
        public override string         Tag { get { return "EDT"; } }
        public override string Description { get { return "Active Soft RGB image format"; } }
        public override uint     Signature { get { return 0x5552542eu; } } // '.TRU'

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EdtFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x22);
            if (!header.AsciiEqual (".TRUE\x8d\x5d\x8c\xcb\x00"))
                return null;
            uint width  = header.ToUInt16 (0x0e);
            uint height = header.ToUInt16 (0x10);
            uint comp_size  = header.ToUInt32 (0x1a);
            uint extra_size = header.ToUInt32 (0x1e);
            if (extra_size % 3 != 0 || 0 == extra_size)
                return null;

            return new EdtMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
                CompSize = comp_size,
                ExtraSize = extra_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (EdtMetaData)info;
            stream.Position = 0x22;
            using (var reader = new Reader (stream.AsStream, meta))
            {
                reader.Unpack();
                return ImageData.Create (meta, PixelFormats.Bgr24, null, reader.Data, (int)meta.Width*3);
            }
        }

        internal sealed class Reader : BitReader, IDisposable
        {
            MemoryStream    m_packed;
            MemoryStream    m_extra;
            byte[]          m_output;
            int[]           ShiftTable = new int[32];

            public byte[] Data { get { return m_output; } }

            public Reader (Stream file, EdtMetaData info)
            {
                byte[] packed = new byte[info.CompSize];
                file.Read (packed, 0, packed.Length);
                m_packed = new MemoryStream (packed, false);

                byte[] extra = new byte[info.ExtraSize];
                file.Read (extra, 0, extra.Length);
                m_extra = new MemoryStream (extra, false);
                m_output = new byte[info.Width*info.Height*3];

                int stride = (int)info.Width * 3;
                int offset = stride * -4;
                int i = 0;
                while (offset != 0)
                {
                    int shift = offset - 9;
                    for (int j = 0; j < 7; ++j)
                    {
                        ShiftTable[i++] = shift;
                        shift += 3;
                    }
                    offset += stride;
                }
                offset = -12;
                while (offset != 0)
                {
                    ShiftTable[i++] = offset;
                    offset += 3;
                }
            }

            public void Unpack ()
            {
                int dst = 0;
                if (3 != m_extra.Read (m_output, dst, 3))
                    throw new InvalidFormatException ("Unexpected end of input");
                dst += 3;
                while (dst < m_output.Length)
                {
                    if (1 == NextBit (m_packed))
                    {
                        if (0 == NextBit (m_packed))
                        {
                            int offset = ShiftTable[ReadBits (0, 5, m_packed)];
                            if (dst < -offset)
                                return;
                            int count = CountBits (m_packed) * 3;
                            Binary.CopyOverlapped (m_output, dst + offset, dst, count);
                            dst += count;
                        }
                        else
                        {
                            int offset = -3;
                            if (1 == NextBit (m_packed))
                            {
                                offset = ShiftTable[(0x11191718 >> ((ReadBits (0, 2, m_packed) << 3)) & 0xFF)];
                                if (dst < -offset)
                                    return;
                            }
                            for (int i = 0; i < 3; ++i)
                            {
                                int b = m_output[dst+offset];
                                if (b < 2)
                                    b = 2;
                                else if (b > 0xfd)
                                    b = 0xfd;
                                if (1 == NextBit (m_packed))
                                {
                                    int b2 = 1 + NextBit (m_packed);
                                    if (0 == NextBit (m_packed))
                                        b -= b2;
                                    else
                                        b += b2;
                                }
                                m_output[dst++] = (byte)b;
                            }
                        }
                    }
                    else
                    {
                        if (3 != m_extra.Read (m_output, dst, 3))
                            throw new InvalidFormatException ("Unexpected end of input");
                        dst += 3;
                    }
                }
            }

            #region IDisposable Members
            bool disposed = false;
            public void Dispose ()
            {
                if (!disposed)
                {
                    m_packed.Dispose();
                    m_extra.Dispose();
                    disposed = true;
                }
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }

    [Export(typeof(ImageFormat))]
    public class Ed8Format : ImageFormat
    {
        public override string         Tag { get { return "ED8"; } }
        public override string Description { get { return "Active Soft indexed image format"; } }
        public override uint     Signature { get { return 0x6942382eu; } } // '.8Bi'

        public Ed8Format ()
        {
            Extensions = new string[] { "ed8", "sal" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Ed8Format.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x1A);
            if (!header.AsciiEqual (".8Bit\x8d\x5d\x8c\xcb\x00"))
                return null;
            uint width  = header.ToUInt16 (0x0e);
            uint height = header.ToUInt16 (0x10);
            int  palette_size = header.ToInt32 (0x12);
            uint comp_size  = header.ToUInt32 (0x16);
            if (palette_size > 0x100)
                return null;

            return new Ed8MetaData
            {
                Width = width,
                Height = height,
                BPP = 8,
                PaletteSize = palette_size,
                CompSize = comp_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (Ed8MetaData)info;
            var reader = new Reader (stream.AsStream, meta);
            reader.Unpack();
            var palette = new BitmapPalette (reader.Palette);
            return ImageData.Create (info, PixelFormats.Indexed8, palette, reader.Data, (int)info.Width);
        }

        internal class Reader : BitReader
        {
            Stream          m_input;
            byte[]          m_data;
            Color[]         m_palette;
            int             m_width;

            public Color[] Palette { get { return m_palette; } }
            public byte[]     Data { get { return m_data; } }

            private static readonly sbyte[] ShiftTable = new sbyte[] {
            //  (-1,0) (0,1)  (-2,0) (-1,1)  (1,1) (0,2)  (-2,1) (2,1)
                -0x10,  0x01, -0x20, -0x0F,  0x11,  0x02, -0x1F,  0x21,
            //  (-2,2) (-1,2) (1,2)  (2,2)   (0,3) (-1,3)
                -0x1E, -0x0E,  0x12,  0x22,  0x03, -0x0D
            };

            public Reader (Stream file, Ed8MetaData info)
            {
                m_width = (int)info.Width;
                m_input = file;
                m_data = new byte[info.Width * info.Height];
            }

            public void Unpack ()
            {
                m_input.Position = 0x1A;
                m_palette = ReadColorMap (m_input, 0x100, PaletteFormat.Bgr);
                int data_pos = 0;
                while (data_pos < m_data.Length)
                {
                    m_data[data_pos++] = (byte)ReadBits (0, 8, m_input);
                    if (m_data.Length == data_pos)
                        break;
                    if (1 == NextBit (m_input))
                        continue;
                    int prev_code = -1;
                    while (data_pos < m_data.Length)
                    {
                        int code = 0;
                        if (1 == NextBit (m_input))
                        {
                            if (1 == NextBit (m_input))
                                code = NextBit (m_input) + 1;
                            code = (code << 1) + NextBit (m_input) + 1;
                        }
                        code = (code << 1) + NextBit (m_input);
                        if (code == prev_code)
                            break;
                        prev_code = code;
                        int count = CountBits (m_input);
                        if (prev_code >= 2)
                           ++count;
                        if (data_pos + count > m_data.Length)
                            throw new InvalidFormatException();
                        int shift = ShiftTable[prev_code];
                        int offset = (shift >> 4) - (shift & 0xf) * m_width;
                        Binary.CopyOverlapped (m_data, data_pos+offset, data_pos, count);
                        data_pos += count;
                    }
                }
            }
        }
    }
}
