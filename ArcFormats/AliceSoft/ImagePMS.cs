//! \file       ImagePMS.cs
//! \date       2017 Nov 26
//! \brief      AliceSoft image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.AliceSoft
{
    internal class PmsMetaData : ImageMetaData
    {
        public uint DataOffset;
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PmsFormat : ImageFormat
    {
        public override string         Tag { get { return "PMS"; } }
        public override string Description { get { return "AliceSoft image format"; } }
        public override uint     Signature { get { return 0x014D50; } } // 'PM'

        public PmsFormat ()
        {
            Signatures = new uint[] { 0x014D50, 0x024D50 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            var info = new PmsMetaData {
                BPP = header[6],
                OffsetX = header.ToInt32 (0x10),
                OffsetY = header.ToInt32 (0x14),
                Width = header.ToUInt32 (0x18),
                Height = header.ToUInt32 (0x1C),
                DataOffset = header.ToUInt32 (0x20),
                AlphaOffset = header.ToUInt32 (0x24),
            };
            if ((info.BPP != 16 && info.BPP != 8) || info.DataOffset < 0x30 || info.DataOffset >= file.Length)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pms = new PmsReader (file, (PmsMetaData)info);
            var bitmap = pms.Unpack();
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PmsFormat.Write not implemented");
        }
    }

    internal class PmsReader
    {
        IBinaryStream   m_input;
        PmsMetaData     m_info;
        int             m_width;
        int             m_height;

        public PmsReader (IBinaryStream input, PmsMetaData info)
        {
            m_input = input;
            m_info = info;
            m_width = (int)m_info.Width;
            m_height = (int)m_info.Height;
        }

        public BitmapSource Unpack ()
        {
            switch (m_info.BPP)
            {
            case 16:    return UnpackRgb();
            case 8:     return UnpackIndexed();
            default:    throw new InvalidFormatException();
            }
        }

        BitmapSource UnpackIndexed ()
        {
            m_input.Position = m_info.AlphaOffset;
            var palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Rgb);
            m_input.Position = m_info.DataOffset;
            var pixels = Unpack8bpp();
            return BitmapSource.Create (m_width, m_height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                        PixelFormats.Indexed8, palette, pixels, m_width);
        }

        BitmapSource UnpackRgb ()
        {
            m_input.Position = m_info.DataOffset;
            var pixels = Unpack16bpp();
            var source = BitmapSource.Create (m_width, m_height, ImageData.DefaultDpiX, ImageData.DefaultDpiY,
                                              PixelFormats.Bgr565, null, pixels, m_width*2);
            if (0 == m_info.AlphaOffset)
                return source;

            m_input.Position = m_info.AlphaOffset;
            var alpha = Unpack8bpp();
            source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);
            var output = new WriteableBitmap (source);
            output.Lock();
            unsafe
            {
                byte* buffer = (byte*)output.BackBuffer;
                int stride = output.BackBufferStride;
                int asrc = 0;
                for (int y = 0; y < m_height; ++y)
                {
                    for (int x = 3; x < stride; x += 4)
                    {
                        buffer[x] = alpha[asrc++];
                    }
                    buffer += stride;
                }
            }
            output.AddDirtyRect (new Int32Rect (0, 0, m_width, m_height));
            output.Unlock();
            return output;
        }

        ushort[] Unpack16bpp ()
        {
            var output = new ushort[m_width * m_height];
            int stride = m_width;

            for (int y = 0; y < m_height; ++y)
            for (int x = 0; x < m_width; )
            {
                int dst = y * stride + x;
                int count = 1;
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0xF8)
                {
                    byte px = m_input.ReadUInt8();
                    output[dst] = (ushort)(ctl | (px << 8));
                }
                else if (ctl == 0xF8)
                {
                    output[dst] = m_input.ReadUInt16();
                }
                else if (ctl == 0xF9)
                {
                    count = m_input.ReadUInt8() + 1;
                    int p0 = m_input.ReadUInt8();
                    int p1 = m_input.ReadUInt8();
                    p0 = ((p0 & 0xE0) << 8) | ((p0 & 0x18) << 6) | ((p0 & 7) << 2);
                    p1 = ((p1 & 0xC0) << 5) | ((p1 & 0x3C) << 3) | (p1 & 3);
                    output[dst] = (ushort)(p0 | p1);
                    for (int i = 1; i < count; i++)
                    {
                        p1 = m_input.ReadUInt8();
                        p1 = ((p1 & 0xC0) << 5) | ((p1 & 0x3C) << 3) | (p1 & 3);
                        output[dst + i] = (ushort)(p0 | p1);
                    }
                }
                else if (ctl == 0xFA)
                {
                    output[dst] = output[dst - stride + 1];
                }
                else if (ctl == 0xFB)
                {
                    output[dst] = output[dst - stride - 1];
                }
                else if (ctl == 0xFC)
                {
                    count = (m_input.ReadUInt8() + 2) * 2;
                    ushort px0 = m_input.ReadUInt16();
                    ushort px1 = m_input.ReadUInt16();
                    for (int i = 0; i < count; i += 2)
                    {
                        output[dst + i    ] = px0;
                        output[dst + i + 1] = px1;
                    }
                }
                else if (ctl == 0xFD)
                {
                    count = m_input.ReadUInt8() + 3;
                    ushort px = m_input.ReadUInt16();
                    for (int i = 0; i < count; i++)
                    {
                        output[dst + i] = px;
                    }
                }
                else if (ctl == 0xFE)
                {
                    count = m_input.ReadUInt8() + 2;
                    int src = dst - stride * 2;
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst+i] = output[src+i];
                    }
                }
                else // ctl == 0xFF
                {
                    count = m_input.ReadUInt8() + 2;
                    int src = dst - stride;
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst+i] = output[src+i];
                    }
                }
                x += count;
            }
            return output;
        }

        byte[] Unpack8bpp ()
        {
            var output = new byte[m_width * m_height];
            int stride = m_width;

            for (int y = 0; y < m_height; y++)
            for (int x = 0; x < m_width; )
            {
                int dst = y * stride + x;
                int count = 1;
                byte ctl = m_input.ReadUInt8();
                if (ctl < 0xF8)
                {
                    output[dst] = ctl;
                }
                else if (ctl == 0xFF)
                {
                    count = m_input.ReadUInt8() + 3;
                    Binary.CopyOverlapped (output, dst - stride, dst, count);
                }
                else if (ctl == 0xFE)
                {
                    count = m_input.ReadUInt8() + 3;
                    Binary.CopyOverlapped (output, dst - stride * 2, dst, count);
                }
                else if (ctl == 0xFD)
                {
                    count = m_input.ReadUInt8() + 4;
                    byte px = m_input.ReadUInt8();
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst + i] = px;
                    }
                }
                else if (ctl == 0xFC)
                {
                    count = (m_input.ReadUInt8() + 3) * 2;
                    byte px0 = m_input.ReadUInt8();
                    byte px1 = m_input.ReadUInt8();
                    for (int i = 0; i < count; i += 2)
                    {
                        output[dst + i    ] = px0;
                        output[dst + i + 1] = px1;
                    }
                }
                else // >= 0xF8 < 0xFC
                {
                    output[dst] = m_input.ReadUInt8();
                }
                x += count;
            }
            return output;
        }
    }
}
