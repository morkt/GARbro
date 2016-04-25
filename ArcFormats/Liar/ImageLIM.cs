//! \file       ImageLIM.cs
//! \date       Fri Apr 15 12:16:20 2016
//! \brief      Liar-soft LIM image format implementation.
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

namespace GameRes.Formats.Liar
{
    internal class LimMetaData : ImageMetaData
    {
        public int Flags;
    }

    [Export(typeof(ImageFormat))]
    public class LimFormat : ImageFormat
    {
        public override string         Tag { get { return "LIM"; } }
        public override string Description { get { return "Liar-soft image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            if (0x4C != stream.ReadByte() || 0x4D != stream.ReadByte())
                return null;
            using (var file = new ArcView.Reader (stream))
            {
                int flag = file.ReadUInt16();
                if ((flag & 0xF) != 2 && (flag & 0xF) != 3)
                    return null;
                int bpp = 0x10 == file.ReadUInt16() ? 16 : 32;
                var meta = new LimMetaData { BPP = bpp, Flags = flag };
                file.ReadUInt16();
                meta.Width  = file.ReadUInt32();
                meta.Height = file.ReadUInt32();
                return meta;
            }
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            using (var reader = new Reader (file, (LimMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, null, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("LimFormat.Write not implemented");
        }

        internal class Reader : IDisposable
        {
            BinaryReader    m_input;
            byte[]          m_output;
            byte[]          m_index;
            byte[]          m_image;
            int             m_width;
            int             m_height;
            int             m_bpp;
            int             m_flags;

            public byte[]        Data { get { return m_image; } }
            public PixelFormat Format { get; private set; }

            public Reader (Stream file, LimMetaData info)
            {
                m_input = new ArcView.Reader (file);
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_flags = info.Flags;
                if (32 == m_bpp)
                    Format = PixelFormats.Bgra32;
                else if (16 == m_bpp)
                    Format = PixelFormats.Bgr565;
                else
                    throw new InvalidFormatException();
                m_image = new byte[m_width*m_height*m_bpp/8];
            }

            int         m_remaining;
            int         m_current;
            int         m_bits;

            public void Unpack ()
            {
                m_input.BaseStream.Position = 0x10;
                if (32 == m_bpp)
                {
                    Unpack32bpp();
                }
                else
                {
                    if (0 != (m_flags & 0x10))
                    {
                        if (0 != (m_flags & 0xE0))
                            Unpack16bpp();
                        else
                            m_input.Read (m_image, 0, m_image.Length);
                    }
                    if (0 != (m_flags & 0x100))
                    {
                        if (0 != (m_flags & 0xE00))
                        {
                            m_output = null;
                            UnpackChannel (3);
                        }
                        else
                            m_output = m_input.ReadBytes (m_width * m_height);
                        ApplyAlpha (m_output);
                    }
                }
            }

            void Unpack32bpp ()
            {
                byte mask = 0xFF;
                for (int i = 3; i >= 0; --i)
                {
                    UnpackChannel (3);
                    int src = 0;
                    for (int p = i; p < m_image.Length; p += 4)
                    {
                        m_image[p] = (byte)(m_output[src++] ^ mask);
                    }
                    mask = 0;
                }
            }

            void Unpack16bpp ()
            {
                int image_size = m_input.ReadInt32();
                m_output = m_image;

                m_remaining = m_input.ReadInt32();
                int index_size = m_input.ReadUInt16() * 2;
                if (null == m_index || index_size > m_index.Length)
                    m_index = new byte[index_size];
                m_input.ReadInt16(); // ignored
                if (index_size != m_input.Read (m_index, 0, index_size))
                    throw new InvalidFormatException ("Unexpected end of file");

                int card;
                if (index_size > 8192)
                {
                    m_index_threshold = 14;
                    m_index_length_limit = 16;
                    card = 4;
                }
                else
                {
                    m_index_threshold = 6;
                    m_index_length_limit = 12;
                    card = 3;
                }
                m_current = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bits = GetBits (card);
                    if (-1 == bits)
                        break;

                    if (0 != bits)
                    {
                        int index = GetIndex (bits);
                        if (index < 0)
                            break;
                        if (dst + 1 >= m_output.Length)
                            break;

                        m_output[dst++] = m_index[index*2];
                        m_output[dst++] = m_index[index*2+1];
                    }
                    else
                    {
                        int count = GetBits (4);
                        if (-1 == count)
                            break;

                        bits = GetBits (card);
                        if (-1 == bits)
                            break;

                        int index = GetIndex (bits);
                        if (-1 == index)
                            break;
                        count += 2;
                        index *= 2;
                        for (int i = 0; i < count; i++)
                        {
                            if (dst + 1 >= m_output.Length)
                                return;
                            m_output[dst++] = m_index[index];
                            m_output[dst++] = m_index[index+1];
                        }
                    }
                }
            }

            void ApplyAlpha (byte[] alpha)
            {
                var pixels = new byte[m_width*m_height*4];
                int alpha_src = 0;
                int dst = 0;
                for (int i = 0; i < m_image.Length; i += 2)
                {
                    int color = LittleEndian.ToUInt16 (m_image, i);
                    pixels[dst++] = (byte)((color & 0x001F) * 0xFF / 0x1F);
                    pixels[dst++] = (byte)((color & 0x07E0) * 0xFF / 0x7E0);
                    pixels[dst++] = (byte)((color & 0xF800) * 0xFF / 0xF800);
                    pixels[dst++] = (byte)~alpha[alpha_src++];
                }
                m_image = pixels;
                Format = PixelFormats.Bgra32;
            }

            void UnpackChannel (int card)
            {
                m_index_threshold = 6;
                m_index_length_limit = 12;

                int channel_size = m_input.ReadInt32();
                if (null == m_output || m_output.Length < channel_size)
                    m_output = new byte[channel_size];
                m_remaining = m_input.ReadInt32();

                int index_size = m_input.ReadUInt16();
                if (null == m_index || index_size > m_index.Length)
                    m_index = new byte[index_size];
                m_input.ReadInt16(); // ignored
                if (index_size != m_input.Read (m_index, 0, index_size))
                    throw new InvalidFormatException ("Unexpected end of file");

                m_current = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    int bits = GetBits (card);
                    if (-1 == bits)
                        break;

                    if (0 != bits)
                    {
                        int index = GetIndex (bits);
                        if (index < 0)
                            break;
                        if (dst + 1 >= m_output.Length)
                            break;

                        m_output[dst++] = m_index[index];
                    }
                    else
                    {
                        int count = GetBits (4);
                        if (-1 == count)
                            break;

                        bits = GetBits (card);
                        if (-1 == bits)
                            break;

                        int index = GetIndex (bits);
                        if (-1 == index)
                            break;
                        count += 2;
                        for (int i = 0; i < count; i++)
                        {
                            if (dst >= m_output.Length)
                                return;
                            m_output[dst++] = m_index[index];
                        }
                    }
                }
            }

            private int GetBits (int n)
            {
                int v = 0;
                while (n > 0)
                {
                    if (0 == m_current)
                    {
                        if (0 == m_remaining)
                            return 0;
                        m_bits = m_input.ReadByte();
                        --m_remaining;
                        m_current = 8;
                    }
                    v <<= 1;
                    m_bits <<= 1;
                    v |= (m_bits >> 8) & 1;
                    --m_current;
                    --n;
                }
                return v;
            }

            int m_index_threshold;
            int m_index_length_limit;

            private int GetIndex (int bits)
            {
                if (bits <= m_index_threshold)
                {
                    if (0 == bits)
                        return -1;
                    if (1 == bits--)
                        return GetBits (1);
                    return (1 << bits) | GetBits (bits);
                }
                for (int i = m_index_threshold; i < m_index_length_limit; ++i)
                {
                    bits = GetBits (1);
                    if (-1 == bits)
                        return -1;
                    if (0 == bits)
                        return (1 << i) | GetBits (i);
                }
                return -1;
            }

            #region IDisposable Members
            bool disposed = false;

            public void Dispose ()
            {
                Dispose (true);
                GC.SuppressFinalize (this);
            }

            protected virtual void Dispose (bool disposing)
            {
                if (!disposed)
                {
                    if (disposing)
                        m_input.Dispose();
                }
            }
            #endregion
        }
    }
}
