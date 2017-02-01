//! \file       ImageBC.cs
//! \date       Sat Jan 28 18:21:09 2017
//! \brief      Image format by Tanaka Tatsuhiro.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Will
{
    internal class TxMetaData : BmpMetaData
    {
        public int  Colors;
        public int  Stride;
        public long DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BcFormat : ImageFormat
    {
        public override string         Tag { get { return "BC"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x12);
            if (!header.AsciiEqual ("BC"))
                return null;
            uint data_offset = header.ToUInt32 (0xA);
            uint width  = file.ReadUInt32();
            uint height = file.ReadUInt32();
            file.ReadInt16();
            int bpp = file.ReadInt16();
            file.Position = 0x2E;
            int colors = file.ReadInt32();
            file.Position = data_offset;
            if (file.ReadUInt32() != 0x34305854) // 'TX04'
                return null;
            int stride = file.ReadUInt16();
            if (height != file.ReadUInt16())
                return null;
            return new TxMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                Stride = stride,
                DataOffset = file.Position,
                Colors = colors <= 0 ? 0x100 : colors,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TxMetaData)info;
            PixelFormat format;
            BitmapPalette palette = null;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == info.BPP)
                format = PixelFormats.Bgra32;
            else if (16 == info.BPP)
                format = PixelFormats.Bgr555;
            else if (8 == info.BPP)
            {
                format = PixelFormats.Indexed8;
                file.Position = 0x36;
                palette = ReadPalette (file.AsStream, meta.Colors);
            }
            else
                throw new InvalidFormatException();
            var reader = new TxReader (file, meta);
            var pixels = reader.Unpack();
            return ImageData.CreateFlipped (info, format, palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BcFormat.Write not implemented");
        }
    }

    internal class TxReader
    {
        IBinaryStream   m_input;
        TxMetaData      m_info;
        byte[]          m_output;

        public byte[]           Data { get { return m_output; } }
        public int            Stride { get { return m_info.Stride; } }

        public TxReader (IBinaryStream input, TxMetaData info)
        {
            m_input = input;
            m_info = info;
            m_output = new byte[m_info.Stride * (int)m_info.Height];
        }

        public byte[] Unpack ()
        {
            m_input.Position = m_info.DataOffset;
            Decompress();
            int pixel_size = m_info.BPP / 8;
            if (pixel_size > 1)
            {
                for (int row = 0; row < m_output.Length; row += m_info.Stride)
                {
                    int dst = row;
                    for (uint x = 1; x < m_info.Width; ++x)
                    {
                        for (int i = 0; i < pixel_size; ++i)
                            m_output[dst+pixel_size+i] += m_output[dst+i];
                        dst += pixel_size;
                    }
                }
            }
            return m_output;
        }

        void Decompress ()
        {
            m_input.Read (m_output, 0, 2);
            int dst = 2;
            while (dst < m_output.Length)
            {
                int count = m_input.ReadByte();
                if (-1 == count)
                    break;
                if (0xE0 == (count & 0xE0))
                {
                    count = Math.Min ((count & 0x1F) + 1, m_output.Length - dst);
                    m_input.Read (m_output, dst, count);
                    dst += count;
                    continue;
                }
                int offset, src;
                if (0xC0 == (count & 0xE0))
                {
                    // this is how it is in the original code
                    offset = (count + (m_input.ReadUInt8() << 5)) & 0x1F;
                    count = m_input.ReadUInt8();
                    src = dst - 1 - offset;
                }
                else if (0 == (count & 0xC0))
                {
                    offset = (count >> 3) & 7;
                    count &= 7;
                    if (count != 7)
                        count += 2;
                    else
                        count = m_input.ReadUInt8();
                    src = dst - 1 - offset;
                }
                else if (0x40 == (count & 0xC0))
                {
                    offset = (count >> 2) & 0xF;
                    count &= 3;
                    if (count != 3)
                        count += 2;
                    else
                        count = m_input.ReadUInt8();
                    src = dst - Stride + offset - 8;
                }
                else
                {
                    offset = (count >> 2) & 0xF;
                    count &= 3;
                    if (count != 3)
                        count += 2;
                    else
                        count = m_input.ReadUInt8();
                    src = dst - Stride * 2 + offset - 8;
                }
                count = Math.Min (count, m_output.Length - dst);
                Binary.CopyOverlapped (m_output, src, dst, count);
                dst += count;
            }
        }
    }
}
