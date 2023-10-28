//! \file       ImagePIC.cs
//! \date       2023 Sep 25
//! \brief      Grocer image format (PC-98).
//
// Copyright (C) 2023 by morkt
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

// [941209][Grocer] Wedding Errantry -Gyakutama Ou-

namespace GameRes.Formats.Grocer
{
    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag => "PIC/GROCER";
        public override string Description => "Grocer image format";
        public override uint     Signature => 1;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x57);
            if (!header.AsciiEqual (0x10, "Actor98"))
                return null;
            uint width = (uint)header.ToUInt16 (0x53) << 3;
            if (width > 640)
                return null;
            return new ImageMetaData
            {
                Width = width,
                Height = header.ToUInt16 (0x55),
                BPP = 4,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new PicReader (file, info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PicFormat.Write not implemented");
        }
    }

    internal class PicReader
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;

        public PicReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x21;
            var palette = ReadPalette();
            m_input.Position = 0x57;
            int stride = m_info.iWidth / 8;
            var pixels = new byte[m_info.iWidth * m_info.iHeight];
            var buffer = new byte[0x3C0];
            int output_pos = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int x;
                for (int plane = 0; plane < 4; ++plane)
                {
                    x = 0;
                    while (x < stride)
                    {
                        byte cur_byte = m_input.ReadUInt8();
                        if (cur_byte > 0 && cur_byte < 6)
                        {
                            int count = m_input.ReadUInt8();
                            switch (cur_byte)
                            {
                            case 1:
                                {
                                    cur_byte = m_input.ReadUInt8();
                                    int dst = plane * 0x50 + x + 0x280;
                                    for (int i = 0; i < count; ++i)
                                    {
                                        buffer[dst+i] = cur_byte;
                                    }
                                    break;
                                }
                            case 2:
                                {
                                    int src = plane * 0x50 + x;
                                    int dst = src + 0x280;
                                    Buffer.BlockCopy (buffer, src, buffer, dst, count);
                                    break;
                                }
                            case 3:
                                {
                                    int src = x + 0x280;
                                    int dst = plane * 0x50 + src;
                                    Buffer.BlockCopy (buffer, src, buffer, dst, count);
                                    break;
                                }
                            case 4:
                                {
                                    int src = x + 0x2D0;
                                    int dst = plane * 0x50 + x + 0x280;
                                    Buffer.BlockCopy (buffer, src, buffer, dst, count);
                                    break;
                                }
                            case 5:
                                {
                                    int src = x + 0x320;
                                    int dst = plane * 0x50 + x + 0x280;
                                    Buffer.BlockCopy (buffer, src, buffer, dst, count);
                                    break;
                                }
                            }
                            x += count;
                        }
                        else
                        {
                            if (6 == cur_byte)
                            {
                                cur_byte = m_input.ReadUInt8();
                            }
                            int dst = plane * 0x50 + x + 0x280;
                            buffer[dst] = cur_byte;
                            ++x;
                        }
                    }
                }
                for (x = 0; x < stride; ++x)
                {
                    byte mask = 0x80;
                    for (int i = 0; i < 8; ++i)
                    {
                        byte px = 0;
                        if ((buffer[x + 0x280] & mask) != 0) px |= 0x01;
                        if ((buffer[x + 0x2D0] & mask) != 0) px |= 0x02;
                        if ((buffer[x + 0x320] & mask) != 0) px |= 0x04;
                        if ((buffer[x + 0x370] & mask) != 0) px |= 0x08;
                        pixels[output_pos + (x << 3) + i] = px;
                        mask >>= 1;
                    }
                }
                Buffer.BlockCopy (buffer, 0x140, buffer, 0, 0x280);
                output_pos += m_info.iWidth;
            }
            return ImageData.Create (m_info, PixelFormats.Indexed8, palette, pixels);
        }

        BitmapPalette ReadPalette ()
        {
            const int count = 16;
            var colors = new Color[count];
            for (int i = 0; i < count; ++i)
            {
                byte g = m_input.ReadUInt8();
                byte r = m_input.ReadUInt8();
                byte b = m_input.ReadUInt8();
                colors[i] = Color.FromRgb ((byte)(r * 0x11), (byte)(g * 0x11), (byte)(b * 0x11));
            }
            return new BitmapPalette (colors);
        }
    }
}
