//! \file       ImageEPA.cs
//! \date       Fri May 01 03:34:26 2015
//! \brief      Pajamas Adventure System image format.
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
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Pajamas
{
    internal class EpaMetaData : ImageMetaData
    {
        public int Mode;
        public int ColorType;
    }

    [Export(typeof(ImageFormat))]
    public class EpaFormat : ImageFormat
    {
        public override string         Tag { get { return "EPA"; } }
        public override string Description { get { return "Pajamas Adventure System image"; } }
        public override uint     Signature { get { return 0x01015045u; } } // 'EP'

        public EpaFormat ()
        {
            Signatures = new uint[] { 0x01015045u, 0x02015045u };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EpaFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (16);
            var info = new EpaMetaData {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (12),
                Mode   = header[3],
                ColorType = header[4],
            };
            switch (info.ColorType)
            {
            case 0: info.BPP = 8; break;
            case 1: info.BPP = 24; break;
            case 2: info.BPP = 32; break;
            case 3: info.BPP = 16; break;
            case 4: info.BPP = 8; break;
            default: return null;
            }
            if (2 == info.Mode)
            {
                info.OffsetX = file.ReadInt32();
                info.OffsetY = file.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Reader (file, (EpaMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            private IBinaryStream   m_input;
            private int     m_start_pos;
            private int     m_width;
            private int     m_height;
            private int     m_pixel_size;
            private byte[]  m_output;
            private bool    m_has_alpha;
            
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (IBinaryStream stream, EpaMetaData info)
            {
                m_input = stream;
                switch (info.ColorType)
                {
                case 0: m_pixel_size = 1; Format = PixelFormats.Indexed8; break;
                case 1: m_pixel_size = 3; Format = PixelFormats.Bgr24; break;
                case 2: m_pixel_size = 4; Format = PixelFormats.Bgra32; break;
                case 3: m_pixel_size = 2; Format = PixelFormats.Bgr565; break;
                case 4: m_pixel_size = 1; Format = PixelFormats.Bgra32; m_has_alpha = true; break;
                default: throw new NotSupportedException ("Not supported EPA color depth");
                }
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_output = new byte[info.Width*info.Height*m_pixel_size];
                m_start_pos = 2 == info.Mode ? 0x18 : 0x10;
            }

            int[] m_offset_table = new int[16];

            public void Unpack ()
            {
                m_input.Position = m_start_pos;
                if (1 == m_pixel_size)
                    Palette = ImageFormat.ReadPalette (m_input.AsStream, 0x100, PaletteFormat.Bgr);
                m_offset_table[0] = 0;
                m_offset_table[1] = 1;
                m_offset_table[2] = m_width;
                m_offset_table[3] = m_width + 1;	
                m_offset_table[4] = 2;
                m_offset_table[5] = m_width - 1;
                m_offset_table[6] = m_width * 2;
                m_offset_table[7] = 3;	
                m_offset_table[8] = (m_width + 1) * 2;
                m_offset_table[9] = m_width + 2;
                m_offset_table[10] = m_width * 2 + 1;
                m_offset_table[11] = m_width * 2 - 1;	
                m_offset_table[12] = (m_width - 1) * 2;
                m_offset_table[13] = m_width - 2;
                m_offset_table[14] = m_width * 3;
                m_offset_table[15] = 4;	

                UnpackChannel (m_output);
                if (m_has_alpha)
                {
                    var alpha = new byte[m_output.Length];
                    UnpackChannel (alpha);
                    var bitmap = new byte[m_width * m_height * 4];
                    int dst = 0;
                    for (int src = 0; src < m_output.Length; ++src)
                    {
                        var color = Palette.Colors[m_output[src]];
                        bitmap[dst++] = color.B;
                        bitmap[dst++] = color.G;
                        bitmap[dst++] = color.R;
                        bitmap[dst++] = alpha[src];
                    }
                    m_output = bitmap;
                }
                if (m_pixel_size > 1)
                {
                    var bitmap = new byte[m_output.Length];
                    int src = 0;
                    if (m_pixel_size > 2)
                    {
                        int stride = m_width * m_pixel_size;
                        for (int p = 0; p < m_pixel_size; ++p)
                        {
                            for (int y = 0; y < m_height; ++y)
                            {
                                int dst = y * stride + p;
                                for (int x = 0; x < m_width; ++x)
                                {
                                    bitmap[dst] = m_output[src++];
                                    dst += m_pixel_size;
                                }
                            }
                        }
                    }
                    else
                    {
                        int dst = 0;
                        int channel_size = m_width * m_height;
                        for (int y = 0; y < m_height; ++y)
                        {
                            for (int x = 0; x < m_width; ++x)
                            {
                                byte c1 = m_output[src + x];
                                byte c2 = m_output[src + x + channel_size];
                                bitmap[dst++] = (byte)(c2 & 3 | (c1 & 7 | (c2 & 0xFC) << 1) << 2);
                                bitmap[dst++] = (byte)(c1 & 0xC0 | (c2 & 0xE3 | (c1 >> 1) & 0x1C) >> 2);
                            }
                            src += m_width;
                        }
                    }
                    m_output = bitmap;
                }
            }

            void UnpackChannel (byte[] output)
            {
                int dst = 0;
                while (dst < output.Length)
                {
                    int count;
                    int flag = m_input.ReadUInt8();
                    if (0 == (flag & 0xF0))
                    {
                        count = flag;
                        if (dst + count > output.Length)
                            count = output.Length - dst;
                        if (count != m_input.Read (output, dst, count))
                            throw new InvalidFormatException ("Unexpected end of file");
                    }
                    else
                    {
                        if (0 != (flag & 8))
                        {
                            count = m_input.ReadUInt8();
                            count += (flag & 7) << 8;
                        }
                        else
                            count = flag & 7;
                        if (dst + count > output.Length)
                            break;
                        Binary.CopyOverlapped (output, dst-m_offset_table[flag >> 4], dst, count);
                    }
                    dst += count;
                }
            }
        }
    }
}
