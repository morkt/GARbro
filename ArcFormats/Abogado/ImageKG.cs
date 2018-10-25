//! \file       ImageKG.cs
//! \date       Wed Oct 19 15:51:18 2016
//! \brief      AbogadoPowers image format.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Abogado
{
    internal class KgMetaData : ImageMetaData
    {
        public int  PaletteOffset;
        public int  DataOffset;
        public int  AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class KgFormat : ImageFormat
    {
        public override string         Tag { get { return "KG/ABOGADO"; } }
        public override string Description { get { return "AbogadoPowers image format"; } }
        public override uint     Signature { get { return 0; } }

        public KgFormat ()
        {
            Signatures = new uint[] { 0x0202474B, 0x0102474B, 0x0200474B, 0x0100474B };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x30);
            return new KgMetaData
            {
                Width   = header.ToUInt16 (4),
                Height  = header.ToUInt16 (6),
                BPP     = header[3] == 2 ? 24 : 8,
                PaletteOffset   = header.ToInt32 (0xC),
                DataOffset      = header.ToInt32 (0x10),
                AlphaOffset     = header[2] == 2 ? header.ToInt32 (0x2C) : 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new KgReader (file, (KgMetaData)info))
            {
                reader.Unpack();
                return ImageData.CreateFlipped (info, reader.Format, reader.Palette, reader.Pixels, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KgFormat.Write not implemented");
        }
    }

    internal sealed class KgReader : IDisposable
    {
        IBinaryStream       m_input;
        MsbBitStream        m_bits;
        KgMetaData          m_info;
        byte[]              m_output;
        int                 m_pixel_size;
        int                 m_stride;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]         Pixels { get { return m_output; } }
        public int            Stride { get { return m_stride; } }

        public KgReader (IBinaryStream input, KgMetaData info)
        {
            m_input = input;
            m_bits = new MsbBitStream (input.AsStream, true);
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            m_stride = m_pixel_size * (int)m_info.Width;
            m_output = new byte[m_stride * (int)m_info.Height];
            if (0 != m_info.AlphaOffset)
                Format = PixelFormats.Bgra32;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else
                Format = PixelFormats.Indexed8;
        }

        public void Unpack ()
        {
            if (8 == m_info.BPP)
            {
                m_input.Position = m_info.PaletteOffset;
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
            }
            m_bits.Input.Position = m_info.DataOffset;
            ResetDict();
            UnpackChannel (0);
            if (m_pixel_size > 1)
            {
                UnpackChannel (1);
                UnpackChannel (2);
            }
            if (m_info.AlphaOffset != 0)
            {
                ConvertToBgr32();
                try
                {
                    m_bits.Input.Position = m_info.AlphaOffset;
                    m_bits.Reset();
                    ResetDict();
                    UnpackChannel (3);
                }
                catch
                {
                    Format = PixelFormats.Bgr32;
                }
            }
        }

        byte[] m_dict = new byte[0x800];

        void ResetDict ()
        {
            for (int i = 0; i < 0x800; ++i)
                m_dict[i] = (byte)(i & 7);
        }

        void ConvertToBgr32 ()
        {
            m_stride = (int)m_info.Width * 4;
            var pixels = new byte[m_stride * (int)m_info.Height];
            int dst = 0;
            if (1 == m_pixel_size)
            {
                var colors = Palette.Colors;
                for (int src = 0; src < m_output.Length; ++src)
                {
                    var pixel = colors[m_output[src]];
                    pixels[dst]   = pixel.B;
                    pixels[dst+1] = pixel.G;
                    pixels[dst+2] = pixel.R;
                    dst += 4;
                }
            }
            else
            {
                for (int src = 0; src < m_output.Length; src += m_pixel_size)
                {
                    pixels[dst]   = m_output[src];
                    pixels[dst+1] = m_output[src+1];
                    pixels[dst+2] = m_output[src+2];
                    dst += 4;
                }
            }
            m_output = pixels;
            m_pixel_size = 4;
        }

        void UnpackChannel (int dst)
        {
            m_output[dst] = (byte)m_bits.GetBits (8);
            dst += m_pixel_size;
            m_output[dst] = (byte)m_bits.GetBits (8);
            dst += m_pixel_size;
            while (dst < m_output.Length)
            {
                int ctl = m_bits.GetBits (1);
                if (-1 == ctl)
                    throw new EndOfStreamException();
                if (0 == ctl)
                {
                    byte b = GetPixel (dst);
                    m_output[dst] = b;
                    UpdateDict (b, m_output[dst - m_pixel_size]);
                    dst += m_pixel_size;
                    continue;
                }
                if (0 != m_bits.GetBits (1))
                    ctl = m_bits.GetBits (2);
                else
                    ctl = 4;
                int offset;
                switch (ctl)
                {
                case 0:
                    offset = m_stride;
                    break;
                case 1:
                    offset = m_stride - m_pixel_size;
                    break;
                case 2:
                    offset = m_stride + m_pixel_size;
                    break;
                case 3:
                    offset = 2 * m_pixel_size;
                    break;
                default:
                    offset = m_pixel_size;
                    break;
                }
                int count = GetCount();
                int src = dst - offset;
                for (int i = 0; i < count; ++i)
                {
                    m_output[dst] = m_output[src];
                    dst += m_pixel_size;
                    src += m_pixel_size;
                }
            }
        }

        byte GetPixel (int dst)
        {
            if (1 == m_bits.GetBits (1))
            {
                return (byte)m_bits.GetBits (8);
            }
            else
            {
                int n = 8 * m_output[dst - m_pixel_size];
                return m_dict[n + m_bits.GetBits (3)];
            }
        }

        void UpdateDict (byte b, byte prev)
        {
            int s = 8 * prev;
            int i;
            for (i = 0; i < 8; ++i)
            {
                if (m_dict[s + i] == b)
                    break;
            }
            if (i != 0)
            {
                if (8 == i)
                    i = 7;
                Buffer.BlockCopy (m_dict, s, m_dict, s+1, i);
                m_dict[s] = b;
            }
        }

        int GetCount ()
        {
            int count = m_bits.GetBits (2);
            if (0 == count)
            {
                count = m_bits.GetBits (4);
                if (0 != count)
                {
                    count += 3;
                }
                else
                {
                    count = m_bits.GetBits (8);
                    if (0 == count)
                    {
                        count = m_bits.GetBits (16);
                        if (0 == count)
                        {
                            count  = m_bits.GetBits (16) << 16;
                            count |= m_bits.GetBits (16);
                        }
                    }
                }
            }
            return count;
        }

        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_bits.Dispose();
                _disposed = true;
            }
        }
    }
}
