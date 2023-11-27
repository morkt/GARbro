//! \file       ImagePICT.cs
//! \date       2023 Aug 24
//! \brief      Macintosh picture format.
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

namespace GameRes.Formats.Apple
{
    internal class PictMetaData : ImageMetaData
    {
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class PictFormat : ImageFormat
    {
        public override string         Tag { get => "PICT/MAC"; }
        public override string Description { get => "Apple Macintosh image format"; }
        public override uint     Signature { get => 0; }

        public PictFormat ()
        {
            Signatures = new[] { 0u, 0x54434950u };
            Extensions = new[] { "pct", "pict", "pic" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            int header_pos = 0x200;
            if (file.Signature == 0x54434950) // 'PICT'
                header_pos = 4;
            if (file.Length < header_pos + 0x10)
                return null;
            file.Position = header_pos + 2;
            short top    = file.ReadI16BE();
            short left   = file.ReadI16BE();
            short bottom = file.ReadI16BE();
            short right  = file.ReadI16BE();
            if (file.ReadU16BE() != 0x11)
                return null;
            int version = file.ReadU16BE();
            if (version != 0x2FF)
                return null;
            return new PictMetaData {
                Width  = (uint)(right - left),
                Height = (uint)(bottom - top),
                OffsetX = left,
                OffsetY = top,
                BPP = 32,
                DataOffset = (uint)file.Position,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PictReader (file, (PictMetaData)info);
            var pixels = decoder.Unpack();
            return ImageData.Create (info, decoder.Format, decoder.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PictFormat.Write not implemented");
        }
    }

    internal static class BinaryStreamExtension
    {
        static public short ReadI16BE (this IBinaryStream file)
        {
            return Binary.BigEndian (file.ReadInt16());
        }

        static public ushort ReadU16BE (this IBinaryStream file)
        {
            return Binary.BigEndian (file.ReadUInt16());
        }

        static public int ReadI32BE (this IBinaryStream file)
        {
            return Binary.BigEndian (file.ReadInt32());
        }

        static public uint  ReadU32BE (this IBinaryStream file)
        {
            return Binary.BigEndian (file.ReadUInt32());
        }
    }

    internal class Pixmap
    {
        public short    Version;
        public short    PackType;
        public int      PackSize;
        public int      HorizRes;
        public int      VertRes;
        public short    PixelType;
        public short    BPP;
        public short    CompCount;
        public short    CompSize;
        public int      PlaneBytes;
        public int      Table;

        public void Deserialize (IBinaryStream input)
        {
            Version = input.ReadI16BE();
            PackType = input.ReadI16BE();
            PackSize = input.ReadI32BE();
            HorizRes = input.ReadI32BE() >> 16; // read 2 bytes and skip next 2
            VertRes = input.ReadI32BE() >> 16;
            PixelType = input.ReadI16BE();
            BPP = input.ReadI16BE();
            CompCount = input.ReadI16BE();
            CompSize = input.ReadI16BE();
            PlaneBytes = input.ReadI32BE();
            Table = input.ReadI32BE();
            input.Seek (4, SeekOrigin.Current);
            if (BPP <= 0 || BPP > 32 || CompCount <= 0 || CompCount > 4 || CompSize <= 0)
                throw new InvalidFormatException();
        }
    }

    internal class PictReader
    {
        IBinaryStream   m_input;
        PictMetaData    m_info;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public PictReader (IBinaryStream input, PictMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        bool HasAlpha = false;
        byte[] m_buffer;

        public byte[] Unpack ()
        {
            Color[] colormap = null;
            Pixmap pixmap = null;
            m_input.Position = m_info.DataOffset;

            while (m_input.PeekByte() != -1)
            {
                if ((m_input.Position & 1) != 0)
                    Skip (1);
                int code = m_input.ReadU16BE();
                if (0x00FF == code || 0xFFFF == code) // EOF
                    break;
                switch (code)
                {
                case 0x0000: // NOP
                    continue;

                case 0x0001: // Clip
                    {
                        int length = m_input.ReadU16BE();
                        if (length < 2)
                            throw new InvalidFormatException();
                        Skip (length-2);
                        break;
                    }

                case 0x001E: // DefHilite
                    break;

                case 0x0090:
                case 0x0091:
                case 0x0098:
                case 0x0099:
                case 0x009A:
                case 0x009B: // BitsRect
                    {
                        int stride = 0;
                        if (code != 0x9A && code != 0x9B)
                            stride = m_input.ReadU16BE();
                        else
                            Skip (6);
                        // FIXME we just read the first bitmap and override an existing frame
                        // TODO place bitmap into frame according to its RECT
                        m_info.OffsetY = m_input.ReadI16BE();
                        m_info.OffsetX = m_input.ReadI16BE();
                        m_info.Height = (uint)(m_input.ReadI16BE() - m_info.OffsetY);
                        m_info.Width  = (uint)(m_input.ReadI16BE() - m_info.OffsetX);
                        if (0x9A == code || 0x9B == code || (stride & 0x8000) != 0)
                        {
                            pixmap = new Pixmap();
                            pixmap.Deserialize (m_input);
                            HasAlpha = pixmap.CompCount == 4;
                        }
                        if (code != 0x9A && code != 0x9B)
                        {
                            int colors = 2;
                            int flags = 0;
                            if ((stride & 0x8000) != 0)
                            {
                                Skip (4);
                                flags = m_input.ReadU16BE();
                                colors = m_input.ReadU16BE() + 1;
                            }
                            if (null == colormap)
                                colormap = new Color[colors];
                            if ((stride & 0x8000) != 0)
                            {
                                for (int i = 0; i < colors; i++)
                                {
                                    int c = m_input.ReadU16BE() % colors;
                                    if ((flags & 0x8000) != 0)
                                        c = i;
                                    int r = m_input.ReadU16BE() / 0x101;
                                    int g = m_input.ReadU16BE() / 0x101;
                                    int b = m_input.ReadU16BE() / 0x101;
                                    colormap[c] = Color.FromRgb ((byte)r, (byte)g, (byte)b);
                                }
                            }
                            else
                            {
                                var White = Color.FromRgb (0xFF, 0xFF, 0xFF);
                                for (int i = 0; i < colors; i++)
                                {
                                    colormap[i] = Color.Subtract (White, colormap[i]);
                                }
                            }
                        }
                        Skip (8+8+2);
                        // -> Skip (8); // source RECT
                        //    Skip (8); // destination RECT
                        //    Skip (2); // transfer mode
                        if (code == 0x91 || code == 0x99 || code == 0x9b)
                        {
                            int length = m_input.ReadU16BE();
                            if (length > 2)
                                Skip (length - 2);
                        }
                        if (code != 0x9A && code != 0x9B && (stride & 0x8000) == 0)
                            DecodeRleBitmap (stride, 1);
                        else
                            DecodeRleBitmap (stride, pixmap.BPP);
                        break;
                    }

                case 0x00A1: // LongComment
                    {
                        m_input.ReadU16BE(); // comment type
                        int length = m_input.ReadU16BE();
                        Skip (length);
                        break;
                    }
                case 0x0C00: // Header
                    Skip (0x18);
                    break;

                default:
                    throw new NotSupportedException (string.Format ("Unknown code 0x{0:X4} in PICT stream.", code));
                }
            }
            if (colormap != null)
                Palette = new BitmapPalette (colormap);
            if (null == m_buffer)
                throw new InvalidFormatException();
            SetFormat (pixmap);
            return RepackPixels (pixmap);
        }

        byte[] RepackPixels (Pixmap pixmap)
        {
            int bpp = m_info.BPP;
            if (bpp < 16)
                return m_buffer;
            else if (16 == bpp)
                return Repack16bpp();
            int bytes_per_pixel = bpp / 8;
            int stride = m_info.iWidth * bytes_per_pixel;
            var pixels = new byte[stride * m_info.iHeight];
            int src = 0;
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int dst = y * stride;
                for (int x = 0; x < m_info.iWidth; ++x)
                {
                    if (HasAlpha)
                    {
                        pixels[dst+3] = m_buffer[src];
                        pixels[dst+2] = m_buffer[src+m_info.iWidth];
                        pixels[dst+1] = m_buffer[src+m_info.iWidth*2];
                        pixels[dst]   = m_buffer[src+m_info.iWidth*3];
                    }
                    else
                    {
                        pixels[dst+2] = m_buffer[src];
                        pixels[dst+1] = m_buffer[src+m_info.iWidth];
                        pixels[dst]   = m_buffer[src+m_info.iWidth*2];
                    }
                    ++src;
                    dst += bytes_per_pixel;
                }
                src += (pixmap.CompCount - 1) * m_info.iWidth;
            }
            return pixels;
        }

        byte[] Repack16bpp () // swap 16bit pixels to little-endian order
        {
            for (int p = 1; p < m_buffer.Length; p += 2)
            {
                byte b = m_buffer[p-1];
                m_buffer[p-1] = m_buffer[p];
                m_buffer[p] = b;
            }
            return m_buffer;
        }

        void SetFormat (Pixmap pixmap)
        {
            int bpp = null == pixmap ? 8 : pixmap.BPP;
            if (32 == bpp)
            {
                if (4 == pixmap.CompCount)
                    Format = PixelFormats.Bgra32;
                else
                    Format = PixelFormats.Bgr32;
            }
            else if (24 == bpp)
                Format = PixelFormats.Bgr24;
            else if (16 == bpp)
                Format = PixelFormats.Bgr555;
            else if (8 == bpp)
            {
                if (Palette != null)
                    Format = PixelFormats.Indexed8;
                else
                    Format = PixelFormats.Gray8;
            }
            else
                throw new NotSupportedException (string.Format ("Not supported PICT bitdepth -- {0}bpp", bpp));
            m_info.BPP = bpp;
        }

        void Skip (int amount)
        {
            m_input.Seek (amount, SeekOrigin.Current);
        }

        byte[] m_unpack_buffer = new byte[0x800];
        byte[] m_scanline;

        void DecodeRleBitmap (int stride, int bpp)
        {
            if (bpp < 8)
                throw new NotSupportedException();
            if (bpp <= 8)
                stride &= 0x7fff;
            int width = m_info.iWidth;
            int bytes_per_pixel = 1;
            if (16 == bpp)
            {
                bytes_per_pixel = 2;
                width *= 2;
            }
            else if (32 == bpp)
                width *= HasAlpha ? 4 : 3;
            if (stride == 0)
                stride = width;
            int stride_32bpp = m_info.iWidth * 4;

            int total_bytes = stride_32bpp * m_info.iHeight;
            if (null == m_buffer || m_buffer.Length < total_bytes)
                m_buffer = new byte[total_bytes];
            int scanline_length = stride_32bpp * 2;
            if (null == m_scanline || m_scanline.Length < scanline_length)
            {
                m_scanline = new byte[scanline_length];
            }
            if (stride < 8)
            {
                int dst = 0;
                int row_size = width * (bpp / 8);
                for (int y = 0; y < m_info.iHeight; ++y)
                {
                    m_input.Read (m_buffer, dst, stride);
                    dst += row_size;
                }
                return;
            }
            for (int y = 0; y < m_info.iHeight; ++y)
            {
                int dst = y * width;
                if (stride > 200)
                    scanline_length = m_input.ReadU16BE();
                else
                    scanline_length = m_input.ReadUInt8();
                if (scanline_length >= m_scanline.Length || scanline_length == 0)
                    throw new InvalidFormatException();
                m_input.Read (m_scanline, 0, scanline_length);
                for (int j = 0; j < scanline_length; )
                {
                    if ((m_scanline[j] & 0x80) == 0)
                    {
                        int pixel_count = m_scanline[j] + 1;
                        int count = pixel_count * bytes_per_pixel;
                        int src = j + 1;
                        if ((dst + count) <= total_bytes)
                            Buffer.BlockCopy (m_scanline, src, m_buffer, dst, count);
                        dst += count;
                        j += count + 1;
                    }
                    else
                    {
                        int count = (m_scanline[j] ^ 0xFF) + 2;
                        int src = j + 1;
                        while (count --> 0)
                        {
                            if ((dst + bytes_per_pixel) <= total_bytes)
                                Buffer.BlockCopy (m_scanline, src, m_buffer, dst, bytes_per_pixel);
                            dst += bytes_per_pixel;
                        }
                        j += bytes_per_pixel + 1;
                    }
                }
            }
        }
    }
}
