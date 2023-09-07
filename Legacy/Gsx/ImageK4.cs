//! \file       ImageK4.cs
//! \date       2023 Aug 27
//! \brief      GSX engine image format.
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

// [030825][Mirai] Hoshi no Oujo

namespace GameRes.Formats.Gsx
{
    internal class K4MetaData : ImageMetaData
    {
        public byte AlphaMode;
        public int  FrameCount;
    }

    [Export(typeof(ImageFormat))]
    public class K4Format : ImageFormat
    {
        public override string         Tag => "K4";
        public override string Description => "GSX engine image format";
        public override uint     Signature => 0x0201344B; // 'K4'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual ("K4"))
                return null;
            if (header[2] != 1 || header[3] != 2)
                return null;
            int frame_count = header.ToInt16 (0xC);
            if (frame_count <= 0)
                return null;
            return new K4MetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = header[0xF],
                AlphaMode = header[0xB],
                FrameCount = frame_count,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new K4Reader (file, (K4MetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("K4Format.Write not implemented");
        }
    }

    internal sealed class K4Reader
    {
        IBinaryStream   m_input;
        K4MetaData      m_info;

        public K4Reader (IBinaryStream input, K4MetaData info)
        {
            m_input = input;
            m_info = info;
        }

        int m_stride;
        int m_pixel_size;

        public ImageData Unpack ()
        {
            uint base_offset = 0x30;
            m_input.Position = base_offset;
            m_info.Width  = m_input.ReadUInt16();
            m_info.Height = m_input.ReadUInt16();
            m_input.Seek (8, SeekOrigin.Current);
            int bpp   = m_input.ReadUInt16();
            int flags = m_input.ReadUInt16();
            m_input.Seek (4, SeekOrigin.Current);
            uint alpha_pos = m_input.ReadUInt32();
            m_input.Seek (12, SeekOrigin.Current);
            int ctl_length = m_input.ReadInt32();

            m_pixel_size = bpp / 8;
            m_stride = (m_info.iWidth * m_pixel_size + 3) & ~3;
            var pixels = new byte[m_info.iHeight * m_stride];
            int dst = 0;
            bool do_delta = (flags & 1) != 0;
            var control_bytes = m_input.ReadBytes (ctl_length - 0x10);
            using (var mem  = new MemoryStream (control_bytes))
            using (var ctl  = new MsbBitStream (mem))
            using (var data = new MsbBitStream (m_input.AsStream, true))
            {
                while (dst < pixels.Length)
                {
                    int b = ctl.GetNextBit();
                    if (-1 == b)
                        break;
                    if (b != 0)
                    {
                        if (!do_delta)
                        {
                            pixels[dst++] = (byte)data.GetBits (8);
                        }
                        else if (dst >= m_pixel_size)
                        {
                            pixels[dst] = (byte)(pixels[dst - m_pixel_size] + data.GetBits (9) + 1);
                            ++dst;
                        }
                        else
                        {
                            pixels[dst++] = (byte)data.GetBits (9);
                        }
                    }
                    else
                    {
                        int pos, count;
                        if (ctl.GetNextBit() != 0)
                        {
                            pos   = data.GetBits (14);
                            count = data.GetBits (4) + 3;
                        }
                        else
                        {
                            pos   = data.GetBits (9);
                            count = data.GetBits (3) + 2;
                        }
                        int src = dst - pos - 1;
                        count = Math.Min (count, pixels.Length - dst);
                        if (!do_delta || dst < m_pixel_size)
                        {
                            Binary.CopyOverlapped (pixels, src, dst, count);
                            dst += count;
                        }
                        else
                        {
                            while (count --> 0)
                            {
                                pixels[dst++] = (byte)(pixels[src] + pixels[src + pos - m_pixel_size + 1] - pixels[src - m_pixel_size]);
                                ++src;
                            }
                        }
                    }
                }
            }
            if (0 == alpha_pos)
            {
                if (24 == bpp)
                    return ImageData.CreateFlipped (m_info, PixelFormats.Bgr24, null, pixels, m_stride);
                else
                    return ImageData.CreateFlipped (m_info, PixelFormats.Bgr32, null, pixels, m_stride);
            }
            if (0xFF == m_info.AlphaMode)
                pixels = UnpackAlphaFF (alpha_pos + base_offset, pixels);
            else if (0xFE == m_info.AlphaMode)
                pixels = UnpackAlphaFE (alpha_pos + base_offset, pixels);
            else
                throw new NotSupportedException (string.Format ("Not supported alpha channel mode 0x{0:X2}", m_info.AlphaMode));
            m_stride = m_info.iWidth * 4;
            return ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels, m_stride);
        }

        byte[] UnpackAlphaFF (uint alpha_pos, byte[] pixels)
        {
            m_input.Position = alpha_pos;
            var offsets = new int[m_info.iHeight];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = m_input.ReadInt32();

            var output = new byte[m_info.iWidth * m_info.iHeight * 4];
            int dst = 0;
            for (int y = 0; y < m_info.iHeight; y++)
            {
                m_input.Position = alpha_pos + offsets[y];
                int src = (m_info.iHeight - y - 1) * m_stride;
                int dst_a = dst + 3;
                for (int x = 0; x < m_info.iWidth; ++x)
                {
                    output[dst  ] = pixels[src  ];
                    output[dst+1] = pixels[src+1];
                    output[dst+2] = pixels[src+2];
                    dst += 4;
                    src += m_pixel_size;
                }
                for (int x = 0; x < m_info.iWidth; )
                {
                    byte alpha = m_input.ReadUInt8();
                    int  count = m_input.ReadUInt8();
                    count = Math.Min (count, m_info.iWidth - x);
                    x += count;
                    if (alpha > 0)
                    {
                        alpha = (byte)((alpha * 0xFF) >> 7);
                        while (count --> 0)
                        {
                            output[dst_a] = alpha;
                            dst_a += 4;
                        }
                    }
                    else
                    {
                        dst_a += 4 * count;
                    }
                }
            }
            return output;
        }

        byte[] UnpackAlphaFE (uint alpha_pos, byte[] pixels)
        {
            m_input.Position = alpha_pos;
            var output = new byte[m_info.iWidth * m_info.iHeight * 4];
            int dst = 0;
            for (int y = 0; y < m_info.iHeight; y++)
            {
                int src = (m_info.iHeight - y - 1) * m_stride;
                int dst_a = dst + 3;
                for (int x = 0; x < m_info.iWidth; ++x)
                {
                    output[dst  ] = pixels[src  ];
                    output[dst+1] = pixels[src+1];
                    output[dst+2] = pixels[src+2];
                    dst += 4;
                    src += m_pixel_size;
                }
                for (int x = 0; x < m_info.iWidth; x += 8)
                {
                    byte alpha = m_input.ReadUInt8();
                    int count = Math.Min (8, m_info.iWidth - x);
                    for (int i = 0; i < count; ++i)
                    {
                        output[dst_a] = (byte)-(alpha & 1);
                        dst_a += 4;
                        alpha >>= 1;
                    }
                }
            }
            return output;
        }
    }
}
