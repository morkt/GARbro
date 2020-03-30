//! \file       ImageGP.cs
//! \date       Thu Nov 12 14:00:52 2015
//! \brief      old Eushully image format.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Eushully
{
    internal class GpMetaData : ImageMetaData
    {
        public bool HasAlpha;
        public int  Method;
        public int  ElementSize;
        public int  PixelsPerElement;
        public int  PaletteSize;
    }

    [Export(typeof(ImageFormat))]
    public class GpFormat : ImageFormat
    {
        public override string         Tag { get { return "GP/EUSHULLY"; } }
        public override string Description { get { return "Old Eushully graphic format"; } }
        public override uint     Signature { get { return 0; } }

        public GpFormat ()
        {
            Extensions = new string[] { "gpcf" }; // made-up, real files have no extension
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int alpha_channel = stream.ReadByte();
            int method = stream.ReadByte();
            int align1 = stream.ReadByte();
            int align2 = stream.ReadByte();
            int bpp = stream.ReadByte();
            if (alpha_channel < 0 || alpha_channel > 1 || method < 0 || method > 2
                || align1 < 0 || align1 > 4 || align2 < 0 || align2 > 4
                || bpp < 0 || !(bpp <= 16 || 24 == bpp || 32 == bpp))
                return null;

            int palette_size = stream.ReadInt32();
            uint width = stream.ReadUInt16();
            uint height = stream.ReadUInt16();
            if (palette_size <= 0 || 0 == width || 0 == height || palette_size >= stream.Length)
                return null;
            if (bpp > 0 && bpp <= 8 && palette_size > 0x100)
                return null;
            return new GpMetaData
            {
                Width   = width,
                Height  = height,
                BPP     = bpp == 0 ? 24 : bpp,
                HasAlpha = alpha_channel != 0,
                Method  = method,
                ElementSize = align1,
                PixelsPerElement = align2,
                PaletteSize = palette_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GpMetaData)info;
            using (var reader = new GpReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpFormat.Write not implemented");
        }
    }

    internal sealed class GpReader : IDisposable
    {
        IBinaryStream   m_input;
        GpMetaData      m_info;
        int             m_width;
        int             m_height;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]           Data { get; private set; }
        public int            Stride { get; private set; }

        public GpReader (IBinaryStream input, GpMetaData info)
        {
            m_info = info;
            m_width = (int)m_info.Width;
            m_height = (int)m_info.Height;
            m_input = input;
        }

        public void Unpack ()
        {
            m_input.Position = 0xD;
            switch (m_info.Method)
            {
            case 0: UnpackV0(); break;
            case 1: UnpackV1(); break;
            case 2: UnpackV2(); break;
            default: throw new NotSupportedException ("Not supported GPC image format");
            }
            if (m_info.HasAlpha)
            {
                if (ReadAlpha())
                    Format = PixelFormats.Bgra32;
            }
        }

        void UnpackV0 ()
        {
            var image = m_input.ReadBytes (m_height * m_width * 3);
            Stride = m_width * 4;
            var pixels = new byte[Stride * m_height];
            int src = 0;
            int dst = 0;
            while (src < image.Length)
            {
                pixels[dst++] = image[src+2];
                pixels[dst++] = image[src+1];
                pixels[dst++] = image[src];
                src += 3;
                dst++;
            }
            Data = pixels;
            Format = PixelFormats.Bgr32;
        }

        void UnpackV1 ()
        {
            var palette = m_input.ReadBytes (3 * m_info.PaletteSize);
            if (8 == m_info.BPP && !m_info.HasAlpha)
            {
                SetPalette (palette, m_info.PaletteSize);
                Data = m_input.ReadBytes (m_width*m_height);
                Format = PixelFormats.Indexed8;
                Stride = m_width;
            }
            else
            {
                Data = ReadIndexedImage (palette);
                Format = PixelFormats.Bgr32;
                Stride = m_width * 4;
            }
        }

        byte[] ReadIndexedImage (byte[] palette)
        {
            int rgb_mask = (1 << m_info.BPP) - 1;
            var chunk = new byte[4];
            var pixels = new byte[m_width*m_height*4];
            int dst = 0;
            for (int y = 0; y < m_height; ++y)
            {
                int x = 0;
                while (x < m_width)
                {
                    m_input.Read (chunk, 0, m_info.ElementSize);
                    int color = LittleEndian.ToInt32 (chunk, 0);
                    for (int i = 0; i < m_info.PixelsPerElement & x < m_width; ++i)
                    {
                        int index = 3 * (color & rgb_mask);
                        if (index >= palette.Length)
                            throw new InvalidFormatException();
                        color >>= m_info.BPP;
                        pixels[dst++] = palette[index+2];
                        pixels[dst++] = palette[index+1];
                        pixels[dst++] = palette[index];
                        ++dst;
                        ++x;
                    }
                }
            }
            return pixels;
        }

        void SetPalette (byte[] palette_data, int colors)
        {
            var palette = new Color[colors];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * 3;
                palette[i] = Color.FromRgb (palette_data[c], palette_data[c+1], palette_data[c+2]);
            }
            Palette = new BitmapPalette (palette);
        }

        void UnpackV2 ()
        {
            Stride = m_width * 4;
            var palette = m_input.ReadBytes (3 * m_info.PaletteSize);

            int back1 = m_input.ReadInt32() * 3; // index within palette
            int back2 = m_input.ReadInt32();
            if (back2 < 0)
                back2 += m_info.PaletteSize;
            back2 *= 3;
            m_input.ReadInt32(); // data_size

            int rgb_mask = (1 << m_info.BPP) - 1;

            var pixels = new byte[Stride*m_height];
            int dst = 0;
            var chunk = new byte[4];
            for (int y = 0; y < m_height; ++y)
            {
                int x = 0;
                while (x < m_width)
                {
                    ushort background_length = m_input.ReadUInt16();
                    ushort foreground_length = m_input.ReadUInt16();
                    int color; // index within palette
                    if ((background_length & 0x8000) == 0)
                    {
                        color = back1;
                    }
                    else
                    {
                        color = back2;
                        background_length &= 0x7FFF;
                    }
                    int i;
                    for (i = 0; i < background_length; ++i)
                    {
                        pixels[dst++] = palette[color+2];
                        pixels[dst++] = palette[color+1];
                        pixels[dst++] = palette[color];
                        ++dst;
                        ++x;
                    }
                    i = 0;
                    while (i < foreground_length && x < m_width)
                    {
                        m_input.Read (chunk, 0, m_info.ElementSize);
                        int element = LittleEndian.ToInt32 (chunk, 0);
                        for (int j = 0; j != m_info.PixelsPerElement && x < m_width && i < foreground_length; ++j)
                        {
                            color = 3 * (element & rgb_mask);
                            element >>= m_info.BPP;
                            pixels[dst++] = palette[color+2];
                            pixels[dst++] = palette[color+1];
                            pixels[dst++] = palette[color];
                            ++dst;
                            ++x;
                            ++i;
                        }
                    }
                }
            }
            Data = pixels;
            Format = PixelFormats.Bgr32;
        }

        bool ReadAlpha ()
        {
            var w = m_input.ReadInt32();
            var h = m_input.ReadInt32();
            if (w != m_width || h != m_height)
                return false;
            int i = 3;
            while (i < Data.Length)
            {
                byte alpha = m_input.ReadUInt8();
                int count = m_input.ReadUInt8();
                for (int j = 0; j < count && i < Data.Length; ++j)
                {
                    Data[i] = alpha;
                    i += 4;
                }
            }
            return true;
        }

        #region IDisposable Members
        public void Dispose ()
        {
        }
        #endregion
    }
}
