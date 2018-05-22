//! \file       ImageGRX.cs
//! \date       Wed Jul 15 00:59:44 2015
//! \brief      U-Me Soft image format.
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
using GameRes.Utility;

namespace GameRes.Formats.UMeSoft
{
    internal class GrxMetaData : ImageMetaData
    {
        public bool IsPacked;
        public bool HasAlpha;
        public int  AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GrxFormat : ImageFormat
    {
        public override string         Tag { get { return "GRX"; } }
        public override string Description { get { return "U-Me Soft image format"; } }
        public override uint     Signature { get { return 0x1A585247; } } // 'GRX'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            return ReadInfo (file);
        }

        internal GrxMetaData ReadInfo (IBinaryStream file)
        {
            var info = new GrxMetaData();
            info.IsPacked   = file.ReadByte() != 0;
            info.HasAlpha   = file.ReadByte() != 0;
            info.BPP        = file.ReadUInt16();
            info.Width      = file.ReadUInt16();
            info.Height     = file.ReadUInt16();
            info.AlphaOffset = file.ReadInt32();
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Reader (file.AsStream, (GrxMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrxFormat.Write not implemented");
        }

        internal class Reader
        {
            Stream      m_input;
            byte[]      m_output;
            GrxMetaData m_info;
            int         m_pixel_size;
            int         m_aligned_width;

            public byte[]        Data { get { return m_output; } }
            public PixelFormat Format { get; private set; }
            public int         Stride { get; private set; }

            public Reader (Stream input, GrxMetaData info)
            {
                m_input = input;
                m_info = info;
                m_pixel_size = (m_info.BPP + 7) / 8;
                switch (m_info.BPP)
                {
                case 32:
                    Format = PixelFormats.Bgr32;
                    break;
                case 24:
                    Format = PixelFormats.Bgr32;
                    m_pixel_size = 4;
                    break;
                case 16:
                    Format = PixelFormats.Bgr565;
                    break;
                case 15:
                    Format = PixelFormats.Bgr555;
                    break;
                case 8:
                    Format = PixelFormats.Gray8;
                    break;
                default:
                    throw new InvalidFormatException();
                }
                m_aligned_width = ((int)m_info.Width + 3) & ~3;
                Stride = m_aligned_width * m_pixel_size;
                m_output = new byte[Stride * (int)info.Height];
            }

            public void Unpack ()
            {
                m_input.Position = 0x10;
                if (!m_info.IsPacked)
                    m_input.Read (m_output, 0, m_output.Length);
                else
                    UnpackColorData (m_output, (m_info.BPP + 7) / 8, m_pixel_size);

                if (m_info.HasAlpha && m_info.AlphaOffset > 0)
                {
                    m_input.Position = 0x10 + m_info.AlphaOffset;
                    var alpha = new byte[m_aligned_width * (int)m_info.Height];
                    UnpackColorData (alpha, 1, 1);
                    if (m_info.BPP >= 24)
                    {
                        int dst = 3;
                        int src = 0;
                        for (uint y = 0; y < m_info.Height; ++y)
                        {
                            for (int x = 0; x < m_aligned_width; ++x)
                            {
                                m_output[dst] = alpha[src++];
                                dst += 4;
                            }
                        }
                        Format = PixelFormats.Bgra32;
                    }
                    else if (16 == m_info.BPP)
                        ApplyAlpha16bpp (alpha);
                }
            }

            void ApplyAlpha16bpp (byte[] alpha)
            {
                int dst_stride = m_aligned_width * 4;
                var pixels = new byte[dst_stride * (int)m_info.Height];
                int src = 0;
                int dst = 0;
                int gap = dst_stride - (int)m_info.Width * 4;
                int a = 0;
                for (uint y = 0; y < m_info.Height; ++y)
                {
                    for (int x = 0; x < m_info.Width; ++x)
                    {
                        int pixel = LittleEndian.ToUInt16 (m_output, src + x*2);
                        pixels[dst++] = (byte)((pixel & 0x001F) * 0xFF / 0x001F);
                        pixels[dst++] = (byte)((pixel & 0x07E0) * 0xFF / 0x07E0);
                        pixels[dst++] = (byte)((pixel & 0xF800) * 0xFF / 0xF800);
                        pixels[dst++] = alpha[a+x];
                    }
                    dst += gap;
                    src += Stride;
                    a += m_aligned_width;
                }
                m_pixel_size = 4;
                Stride = dst_stride;
                m_output = pixels;
                Format = PixelFormats.Bgra32;
            }

            static readonly int[,] OffsetTable = new int[2,16] {
                { 0, -1, -1, -1,  0, -2, -2, -2,  0, -4, -4, -4, -2, -2, -4, -4 },
                { 0,  0, -1,  1, -2,  0, -2,  2, -4,  0, -4,  4, -4,  4, -2,  2 },
            };

            void UnpackColorData (byte[] output, int src_pixel_size, int dst_pixel_size)
            {
                int[] offset_step = new int[16];

                int stride = ((int)m_info.Width * dst_pixel_size + 3) & ~3;
                int delta = stride - (int)m_info.Width * dst_pixel_size;
                for (int i = 0; i < 16; i++)
                    offset_step[i] = OffsetTable[0,i] * stride + OffsetTable[1,i] * dst_pixel_size;

                int dst = 0;
                for (uint y = 0; y < m_info.Height; ++y)
                {
                    int w = (int)m_info.Width;
                    while (w > 0)
                    {
                        int flag = m_input.ReadByte();
                        if (-1 == flag)
                            throw new InvalidFormatException();

                        int count = flag & 3;
                        if (0 != (flag & 4))
                        {
                            count |= m_input.ReadByte() << 2;
                        }
                        w -= ++count;
                        if (0 == (flag & 0xF0))
                        {
                            if (0 != (flag & 8))
                            {
                                if (src_pixel_size == dst_pixel_size)
                                {
                                    count *= dst_pixel_size;
                                    if (count != m_input.Read (output, dst, count))
                                        throw new InvalidFormatException();
                                    dst += count;
                                }
                                else
                                {
                                    for (int i = 0; i < count; ++i)
                                    {
                                        if (src_pixel_size != m_input.Read (output, dst, src_pixel_size))
                                            throw new InvalidFormatException();
                                        dst += dst_pixel_size;
                                    }
                                }
                            }
                            else
                            {
                                if (src_pixel_size != m_input.Read (output, dst, src_pixel_size))
                                    throw new InvalidFormatException();
                                --count;
                                dst += dst_pixel_size;
                                for (int i = count*dst_pixel_size; i > 0; i--)
                                {
                                    output[dst] = output[dst-dst_pixel_size];
                                    dst++;
                                }
                            }
                        }
                        else
                        {
                            int src = dst + offset_step[flag >> 4];
                            if (0 == (flag & 8))
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    for (int j = 0; j < src_pixel_size; ++j)
                                        output[dst+j] = output[src+j];
                                    dst += dst_pixel_size;
                                }
                            }
                            else
                            {
                                count *= dst_pixel_size;
                                Binary.CopyOverlapped (output, src, dst, count);
                                dst += count;
                            }
                        }
                    }
                    dst += delta;
                }
            }
        }
    }

    // SGX header format
    // signature            32bit
    // GRX offset           32bit
    // number of frames     32bit
    // ... frames info (* number of frames)
    // x coordinate         16bit
    // y coordinate         16bit
    // frame width          16bit
    // frame height         16bit
    // transparency color   32bit

    internal class SgxMetaData : ImageMetaData
    {
        public int           GrxOffset;
        public ImageMetaData GrxInfo;
    }

    [Export(typeof(ImageFormat))]
    public class SgxFormat : GrxFormat
    {
        public override string         Tag { get { return "SGX"; } }
        public override string Description { get { return "U-Me Soft multi-frame image format"; } }
        public override uint     Signature { get { return 0x1A584753; } } // 'SGX'

        public SgxFormat ()
        {
            Extensions = new string[] { "grx" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int offset = file.ReadInt32();
            if (offset <= 8)
                return null;
            file.Position = offset;
            uint signature = file.ReadUInt32();
            if (signature != base.Signature)
                return null;
            var info = ReadInfo (file);
            return new SgxMetaData
            {
                Width   = info.Width,
                Height  = info.Height,
                BPP     = info.BPP,
                GrxOffset = offset,
                GrxInfo = info
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (SgxMetaData)info;
            using (var grx = new StreamRegion (stream.AsStream, meta.GrxOffset, true))
            {
                var reader = new Reader (grx, (GrxMetaData)meta.GrxInfo);
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data, reader.Stride);
            }
        }
    }
}
