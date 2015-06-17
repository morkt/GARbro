//! \file       ImageCRX.cs
//! \date       Mon Jun 15 15:14:59 2015
//! \brief      Circus image format.
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

namespace GameRes.Formats.Circus
{
    internal class CrxMetaData : ImageMetaData
    {
        public int Mode;
        public int Colors;
    }

    [Export(typeof(ImageFormat))]
    public class CrxFormat : ImageFormat
    {
        public override string         Tag { get { return "CRX"; } }
        public override string Description { get { return "Circus image format"; } }
        public override uint     Signature { get { return 0x47585243; } } // 'CRXG'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x14];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int type = LittleEndian.ToInt32 (header, 0x10);
            var info = new CrxMetaData
            {
                Width = LittleEndian.ToUInt16 (header, 8),
                Height = LittleEndian.ToUInt16 (header, 10),
                BPP = 0 == type ? 24 : 1 == type ? 32 : 8,
                Mode = LittleEndian.ToUInt16 (header, 12),
                Colors = type,
            };
            if (info.Mode != 1 && info.Mode != 2)
                return null;
            return info;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as CrxMetaData;
            if (null == meta)
                throw new ArgumentException ("CrxFormat.Read should be supplied with CrxMetaData", "info");

            stream.Position = 0x14;
            using (var reader = new Reader (stream, meta))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("CrxFormat.Write not implemented");
        }

        internal sealed class Reader : IDisposable
        {
            BinaryReader    m_input;
            byte[]          m_output;
            int             m_width;
            int             m_height;
            int             m_stride;
            int             m_bpp;
            int             m_mode;

            public byte[]           Data { get { return m_output; } }
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }

            public Reader (Stream input, CrxMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_mode = info.Mode;
                switch (m_bpp)
                {
                case 24: Format = PixelFormats.Bgr24; break;
                case 32: Format = PixelFormats.Bgra32; break;
                case 8:  Format = PixelFormats.Indexed8; break;
                default: throw new InvalidFormatException();
                }
                m_stride = (m_width * m_bpp / 8 + 3) & ~3;
                m_output = new byte[m_height*m_stride];
                m_input = new ArcView.Reader (input);
                if (8 == m_bpp)
                    ReadPalette (info.Colors);
            }

            private void ReadPalette (int colors)
            {
                int palette_size = colors * 3;
                var palette_data = new byte[palette_size];
                if (palette_size != m_input.Read (palette_data, 0, palette_size))
                    throw new InvalidFormatException();
                var palette = new Color[colors];
                for (int i = 0; i < palette.Length; ++i)
                {
                    byte r = palette_data[i*3];
                    byte g = palette_data[i*3+1];
                    byte b = palette_data[i*3+2];
                    if (0xff == b && 0 == g && 0xff == r)
                        g = 0xff;
                    palette[i] = Color.FromRgb (r, g, b);
                }
                Palette = new BitmapPalette (palette);
            }

            public void Unpack ()
            {
                if (1 == m_mode)
                    UnpackV1();
                else
                    UnpackV2();

                if (32 == m_bpp)
                {
                    int line = 0;
                    for (int h = 0; h < m_height; h++)
                    {
                        int shift = (h & 1) * 3;

                        for (int w = 0;	w < m_width; w++)
                        {
                            int pixel = line + w * 4;
                            byte alpha = m_output[pixel];
                            int b = m_output[pixel+1];
                            int g = m_output[pixel+2];
                            int r = m_output[pixel+3];

                            if (alpha != 0xff)
                            {
                                b += (w & 1) + shift;
                                if (b < 0)
                                    b = 0;
                                else if (b > 0xff)
                                    b = 0xff;

                                g += (w & 1) + shift;
                                if (g < 0)
                                    g = 0;
                                else if (g > 0xff)
                                    g = 0xff;

                                r += (w & 1) + shift;
                                if (r < 0)
                                    r = 0;
                                else if (r > 0xff)
                                    r = 0xff;
                            }
                            m_output[pixel]   = (byte)b;
                            m_output[pixel+1] = (byte)g;
                            m_output[pixel+2] = (byte)r;
                            m_output[pixel+3] = alpha;
                            shift = -shift;
                        }
                        line += m_stride;
                    }
                }
                else if (24 == m_bpp)
                {
                    int pixel = 0;

                    for (int h = 0; h < m_height; h++)
                    {
                        int shift = (h & 1) * 3;

                        for (int w = 0;	w < m_width; w++)
                        {
                            int b = m_output[pixel];
                            int g = m_output[pixel+1];
                            int r = m_output[pixel+2];
                            if (b != 0xff || 0 != g || r != b)
                            {
                                b += (w & 1) + shift;
                                if (b < 0)
                                    b = 0;
                                else if (b > 0xff)
                                    b = 0xff;

                                g += (w & 1) + shift;
                                if (g < 0)
                                    g = 0;
                                else if (g > 0xff)
                                    g = 0xff;

                                r += (w & 1) + shift;
                                if (r < 0)
                                    r = 0;
                                else if (r > 0xff)
                                    r = 0xff;

                                m_output[pixel]   = (byte)b;
                                m_output[pixel+1] = (byte)g;
                                m_output[pixel+2] = (byte)r;
                            }
                            shift = -shift;
                            pixel += 3;
                        }
                    }
                }
            }

            private void UnpackV1 ()
            {
                byte[] window = new byte[0x10000];
                int flag = 0;
                int win_pos = 0;
                int dst = 0;
                while (dst < m_output.Length)
                {
                    flag >>= 1;
                    if (0 == (flag & 0x100))
                        flag = m_input.ReadByte() | 0xff00;

                    if (0 != (flag & 1))
                    {
                        byte dat = m_input.ReadByte();
                        window[win_pos++] = dat;
                        win_pos &= 0xffff;
                        m_output[dst++] = dat;
                    }
                    else
                    {
                        byte control = m_input.ReadByte();
                        int count, offset;

                        if (control >= 0xc0)
                        {
                            offset = ((control & 3) << 8) | m_input.ReadByte();
                            count = 4 + ((control >> 2) & 0xf);
                        }
                        else if (0 != (control & 0x80))
                        {
                            offset = control & 0x1f;
                            count = 2 + ((control >> 5) & 3);
                            if (0 == offset)
                                offset = m_input.ReadByte();
                        }
                        else if (0x7f == control)
                        {
                            count = 2 + m_input.ReadUInt16();
                            offset = m_input.ReadUInt16();
                        }
                        else
                        {
                            offset = m_input.ReadUInt16();
                            count = control + 4;
                        }
                        offset = win_pos - offset;
                        for (int k = 0; k < count && dst < m_output.Length; k++)
                        {
                            offset &= 0xffff;
                            byte dat = window[offset++];
                            window[win_pos++] = dat;
                            win_pos &= 0xffff;
                            m_output[dst++] = dat;
                        }
                    }
                }
            }

            private void UnpackV2 ()
            {
                throw new NotImplementedException ("CRX v2 not implemented");
            }

            #region IDisposable Members
            bool m_disposed = false;

            public void Dispose ()
            {
                if (!m_disposed)
                {
                    m_input.Dispose();
                    m_disposed = true;
                }
                GC.SuppressFinalize (this);
            }
            #endregion
        }
    }
}
