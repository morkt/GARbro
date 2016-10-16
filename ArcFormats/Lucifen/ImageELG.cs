//! \file       ImageELG.cs
//! \date       Wed Apr 22 03:14:49 2015
//! \brief      Lucifen Easy Game System image format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Lucifen
{
    internal class ElgMetaData : ImageMetaData
    {
        public int Type;
        public int HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class ElgFormat : ImageFormat
    {
        public override string         Tag { get { return "ELG"; } }
        public override string Description { get { return "Lucifen Easy Game System image format"; } }
        public override uint     Signature { get { return 0x01474c45u; } } // 'ELG\001'

        public ElgFormat ()
        {
            Signatures = new uint[] { 0x01474c45u, 0x08474c45u, 0x18474c45u, 0x20474c45u, 0x02474c45u };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("ElgFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 3;
            int bpp = file.ReadByte();
            int x = 0;
            int y = 0;
            int type = bpp;
            int header_size = 8;
            if (2 == type)
            {
                bpp = file.ReadByte();
                header_size = 13;
            }
            else if (1 == type)
            {
                bpp = file.ReadByte();
                x = file.ReadInt16();
                y = file.ReadInt16();
                header_size = 13;
            }
            else
                type = 0;
            if (8 != bpp && 24 != bpp && 32 != bpp)
                return null;
            uint w = file.ReadUInt16();
            uint h = file.ReadUInt16();
            if (2 == type)
            {
                x = file.ReadInt16();
                y = file.ReadInt16();
            }
            return new ElgMetaData
            {
                Width = w,
                Height = h,
                OffsetX = x,
                OffsetY = y,
                BPP = bpp,
                Type = type,
                HeaderSize = header_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ElgMetaData)info;
            file.Position = meta.HeaderSize;
            using (var reader = new Reader (file, meta))
            {
                reader.Unpack();
                return ImageData.Create (meta, reader.Format, reader.Palette, reader.Data);
            }
        }

        internal class Reader : IDisposable
        {
            int             m_width;
            int             m_height;
            int             m_bpp;
            int             m_type;
            IBinaryStream   m_input;
            byte[]          m_output;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (IBinaryStream stream, ElgMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_type = info.Type;
                m_output = new byte[m_width*m_height*m_bpp/8];
                m_input = stream;
            }

            public void Unpack ()
            {
                if (2 == m_type)
                {
                    while (0 != m_input.ReadUInt8())
                    {
                        int size = m_input.ReadInt32();
                        if (size < 4)
                            throw new InvalidFormatException();
                        m_input.Seek (size-4, SeekOrigin.Current);
                    }
                }
                if (8 == m_bpp)
                {
                    Format = PixelFormats.Indexed8;
                    UnpackPalette();
                    UnpackIndexed (m_output);
                }
                else if (24 == m_bpp)
                {
                    Format = PixelFormats.Bgr24;
                    UnpackRGB();
                }
                else
                {
                    Format = PixelFormats.Bgra32;
                    UnpackRGBA();
                    UnpackAlpha();
                }
            }

            void UnpackPalette ()
            {
                byte[] palette_data = new byte[0x400];
                UnpackIndexed (palette_data);
                var colors = new Color[256];
                for (int i = 0; i < 256; ++i)
                    colors[i] = Color.FromRgb (palette_data[i*4+2], palette_data[i*4+1], palette_data[i*4]);
                Palette = new BitmapPalette (colors);
            }

            void UnpackIndexed (byte[] output)
            {
                int dst = 0;
                for (;;)
                {
                    byte flags = m_input.ReadUInt8();
                    if (0xff == flags || dst >= output.Length)
                        break;
                    int count, pos;

                    if (0 == (flags & 0xc0))
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 33;
                        else
                            count = (flags & 0x1f) + 1;

                        m_input.Read (output, dst, count);
                        dst += count;
                    }
                    else if ((flags & 0xc0) == 0x40)
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 35;
                        else
                            count = (flags & 0x1f) + 3;			

                        byte v = m_input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                            output[dst++] = v;
                    }
                    else
                    {
                        if ((flags & 0xc0) == 0x80)
                        {
                            if (0 == (flags & 0x30))
                            {
                                count = (flags & 0xf) + 2;
                                pos = m_input.ReadUInt8() + 2;
                            }
                            else if ((flags & 0x30) == 0x10)
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = m_input.ReadUInt8() + 4;
                            }
                            else if ((flags & 0x30) == 0x20)
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = 3;
                            }
                            else
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = 4;
                            }
                        }
                        else if (0 != (flags & 0x20))
                        {
                            pos = (flags & 0x1f) + 2;
                            count = 2;
                        }
                        else
                        {
                            pos = (flags & 0x1f) + 1;
                            count = 1;
                        }
                        int src = dst - pos;
                        Binary.CopyOverlapped (output, src, dst, count);
                        dst += count;
                    }
                }
            }

            void UnpackRGBA ()
            {
                int dst = 0;
                for (;;)
                {
                    byte flags = m_input.ReadUInt8();
                    if (0xff == flags || dst >= m_output.Length)
                        break;
                    int count, pos, src;

                    if (0 == (flags & 0xc0))
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 33;
                        else
                            count = (flags & 0x1f) + 1;

                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = 0xff;
                        }
                    }
                    else if ((flags & 0xc0) == 0x40)
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 34;
                        else
                            count = (flags & 0x1f) + 2;

                        byte b = m_input.ReadUInt8();
                        byte g = m_input.ReadUInt8();
                        byte r = m_input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = b;
                            m_output[dst++] = g;
                            m_output[dst++] = r;
                            m_output[dst++] = 0xff;
                        }
                    }
                    else if ((flags & 0xc0) == 0x80)
                    {
                        if (0 == (flags & 0x30))
                        {
                            count = (flags & 0xf) + 1;
                            pos = m_input.ReadUInt8() + 2;
                        }
                        else if ((flags & 0x30) == 0x10)
                        {
                            pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 2;
                            count = m_input.ReadUInt8() + 1;
                        }
                        else if ((flags & 0x30) == 0x20)
                        {
                            byte tmp = m_input.ReadUInt8();
                            pos = ((((flags & 0xf) << 8) + tmp) << 8) + m_input.ReadUInt8() + 4098;
                            count = m_input.ReadUInt8() + 1;
                        }
                        else
                        {
                            if (0 != (flags & 8))
                                pos = ((flags & 0x7) << 8) + m_input.ReadUInt8() + 10;
                            else
                                pos = (flags & 0x7) + 2;
                            count = 1;
                        }

                        src = dst - 4 * pos;
                        Binary.CopyOverlapped (m_output, src, dst, count*4);
                        dst += count*4;
                    }
                    else
                    {
                        int y, x;

                        if (0 == (flags & 0x30))
                        {
                            if (0 == (flags & 0xc))
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = 0;
                            }
                            else if ((flags & 0xc) == 0x4)
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = -1;
                            }
                            else if ((flags & 0xc) == 0x8)
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = 1;
                            }
                            else
                            {
                                pos = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 2058;
                                src = dst - 4 * pos;
                                Buffer.BlockCopy (m_output, src, m_output, dst, 4);
                                dst += 4;
                                continue;
                            }
                        }
                        else if ((flags & 0x30) == 0x10)
                        {
                            y = (flags & 0xf) + 1;
                            x = 0;
                        }
                        else if ((flags & 0x30) == 0x20)
                        {
                            y = (flags & 0xf) + 1;
                            x = -1;
                        }
                        else
                        {
                            y = (flags & 0xf) + 1;
                            x = 1;
                        }
                        src = dst + (x - m_width * y) * 4;
                        Buffer.BlockCopy (m_output, src, m_output, dst, 4);
                        dst += 4;
                    }
                }
            }

            public void UnpackAlpha ()
            {
                int dst = 3;
                for (;;)
                {
                    byte flags = m_input.ReadUInt8();
                    if (0xff == flags || dst >= m_output.Length)
                        break;

                    int count, pos;

                    if (0 == (flags & 0xc0))
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 33;
                        else
                            count = (flags & 0x1f) + 1;

                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = m_input.ReadUInt8();
                            dst += 4;
                        }
                    }
                    else if (0x40 == (flags & 0xc0))
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 35;
                        else
                            count = (flags & 0x1f) + 3;

                        byte a = m_input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = a;
                            dst += 4;
                        }
                    }
                    else
                    {
                        if (0x80 == (flags & 0xc0))
                        {
                            if (0 == (flags & 0x30))
                            {
                                count = (flags & 0xf) + 2;
                                pos = m_input.ReadUInt8() + 2;
                            }
                            else if (0x10 == (flags & 0x30))
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = m_input.ReadUInt8() + 4;
                            }
                            else if ((flags & 0x30) == 0x20)
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = 3;
                            }
                            else
                            {
                                pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 3;
                                count = 4;
                            }
                        }
                        else if (0 != (flags & 0x20))
                        {
                            pos = (flags & 0x1f) + 2;
                            count = 2;
                        }
                        else
                        {
                            pos = (flags & 0x1f) + 1;
                            count = 1;
                        }

                        int src = dst - 4 * pos;
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst] = m_output[src];
                            src += 4;
                            dst += 4;
                        }
                    }
                }
            }

            void UnpackRGB ()
            {
                int dst = 0;
                for (;;)
                {
                    byte flags = m_input.ReadUInt8();
                    if (0xff == flags || dst >= m_output.Length)
                        break;
                    int count, pos, src;
                    if (0 == (flags & 0xc0))
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 33;
                        else
                            count = (flags & 0x1f) + 1;

                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                            m_output[dst++] = m_input.ReadUInt8();
                        }
                    }
                    else if ((flags & 0xc0) == 0x40)
                    {
                        if (0 != (flags & 0x20))
                            count = ((flags & 0x1f) << 8) + m_input.ReadUInt8() + 34;
                        else
                            count = (flags & 0x1f) + 2;			

                        byte b = m_input.ReadUInt8();
                        byte g = m_input.ReadUInt8();
                        byte r = m_input.ReadUInt8();
                        for (int i = 0; i < count; ++i)
                        {
                            m_output[dst++] = b;
                            m_output[dst++] = g;
                            m_output[dst++] = r;
                        }
                    }
                    else if ((flags & 0xc0) == 0x80)
                    {
                        if (0 == (flags & 0x30))
                        {
                            count = (flags & 0xf) + 1;
                            pos = m_input.ReadUInt8() + 2;
                        }
                        else if ((flags & 0x30) == 0x10)
                        {
                            pos = ((flags & 0xf) << 8) + m_input.ReadUInt8() + 2;
                            count = m_input.ReadUInt8() + 1;
                        }
                        else if ((flags & 0x30) == 0x20)
                        {
                            byte tmp = m_input.ReadUInt8();
                            pos = ((((flags & 0xf) << 8) + tmp) << 8) + m_input.ReadUInt8() + 4098;
                            count = m_input.ReadUInt8() + 1;
                        }
                        else
                        {
                            if (0 != (flags & 8))
                                pos = ((flags & 0x7) << 8) + m_input.ReadUInt8() + 10;
                            else
                                pos = (flags & 0x7) + 2;
                            count = 1;
                        }

                        src = dst - 3 * pos;
                        Binary.CopyOverlapped (m_output, src, dst, count*3);
                        dst += count*3;
                    }
                    else
                    {
                        int y, x;

                        if (0 == (flags & 0x30))
                        {
                            if (0 == (flags & 0xc))
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = 0;
                            }
                            else if ((flags & 0xc) == 0x4)
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = -1;
                            }
                            else if ((flags & 0xc) == 0x8)
                            {
                                y = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 16;
                                x = 1;
                            }
                            else
                            {
                                pos = ((flags & 0x3) << 8) + m_input.ReadUInt8() + 2058;
                                src = dst - 3 * pos;
                                m_output[dst++] = m_output[src++];
                                m_output[dst++] = m_output[src++];
                                m_output[dst++] = m_output[src];
                                continue;
                            }
                        }
                        else if ((flags & 0x30) == 0x10)
                        {
                            y = (flags & 0xf) + 1;
                            x = 0;
                        }
                        else if ((flags & 0x30) == 0x20)
                        {
                            y = (flags & 0xf) + 1;
                            x = -1;
                        }
                        else
                        {
                            y = (flags & 0xf) + 1;
                            x = 1;
                        }
                        src = dst + (x - m_width * y) * 3;
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src++];
                        m_output[dst++] = m_output[src];
                    }
                }
            }

            #region IDisposable Members
            public void Dispose ()
            {
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
