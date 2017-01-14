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
            var info = new EpaMetaData();
            info.Mode = file.ReadInt32() >> 24;
            info.ColorType = file.ReadInt32() & 0xff;
            switch (info.ColorType)
            {
            case 0: info.BPP = 8; break;
            case 1: info.BPP = 24; break;
            case 2: info.BPP = 32; break;
            case 3: info.BPP = 15; break;
            case 4: info.BPP = 8; break;
            default: return null;
            }
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            if (2 == info.Mode)
            {
                info.OffsetX = file.ReadInt32();
                info.OffsetY = file.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (EpaMetaData)info as EpaMetaData;
            file.Position = 2 == meta.Mode ? 0x18 : 0x10;
            var reader = new Reader (file.AsStream, meta);
            reader.Unpack();
            return ImageData.Create (meta, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            private Stream  m_input;
            private int     m_width;
            private int     m_height;
            private int     m_pixel_size;
            private byte[]  m_output;
            
            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (Stream stream, EpaMetaData info)
            {
                m_input = stream;
                switch (info.ColorType)
                {
                case 0: m_pixel_size = 1; Format = PixelFormats.Indexed8; break;
                case 1: m_pixel_size = 3; Format = PixelFormats.Bgr24; break;
                case 2: m_pixel_size = 4; Format = PixelFormats.Bgra32; break;
                case 3: m_pixel_size = 2; Format = PixelFormats.Bgr555; break;
                case 4: m_pixel_size = 1; Format = PixelFormats.Indexed8; break;
                default: throw new NotSupportedException ("Not supported EPA color depth");
                }
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_output = new byte[info.Width*info.Height*m_pixel_size];
                if (8 == info.BPP)
                    Palette = ImageFormat.ReadPalette (m_input, 0x100, PaletteFormat.Bgr);
            }

            public void Unpack ()
            {
                int dst = 0;
                var offset_table = new int[16];

                offset_table[0] = 0;
                offset_table[1] = 1;
                offset_table[2] = m_width;
                offset_table[3] = m_width + 1;	
                offset_table[4] = 2;
                offset_table[5] = m_width - 1;
                offset_table[6] = m_width * 2;
                offset_table[7] = 3;	
                offset_table[8] = (m_width + 1) * 2;
                offset_table[9] = m_width + 2;
                offset_table[10] = m_width * 2 + 1;
                offset_table[11] = m_width * 2 - 1;	
                offset_table[12] = (m_width - 1) * 2;
                offset_table[13] = m_width - 2;
                offset_table[14] = m_width * 3;
                offset_table[15] = 4;	
                
                while (dst < m_output.Length)
                {
                    int flag = m_input.ReadByte();
                    if (-1 == flag)
                        throw new InvalidFormatException ("Unexpected end of file");

                    if (0 == (flag & 0xf0))
                    {
                        int count = flag;
                        if (dst + count > m_output.Length)
                            count = m_output.Length - dst;
                        if (count != m_input.Read (m_output, dst, count))
                            throw new InvalidFormatException ("Unexpected end of file");
                        dst += count;
                    }
                    else
                    {
                        int count;
                        if (0 != (flag & 8))
                        {
                            count = m_input.ReadByte();
                            if (-1 == count)
                                throw new InvalidFormatException ("Unexpected end of file");
                            count += (flag & 7) << 8;
                        }
                        else
                            count = flag & 7;
                        if (dst + count > m_output.Length)
                            break;
                        Binary.CopyOverlapped (m_output, dst-offset_table[flag >> 4], dst, count);
                        dst += count;
                    }
                }
                if (m_pixel_size > 1)
                {
                    var bitmap = new byte[m_output.Length];
                    int stride = m_width * m_pixel_size;
                    int i = 0;
                    for (int p = 0; p < m_pixel_size; ++p)
                    {
                        for (int y = 0; y < m_height; ++y)
                        {
                            int pixel = y * stride + p;
                            for (int x = 0; x < m_width; ++x)
                            {
                                bitmap[pixel] = m_output[i++];
                                pixel += m_pixel_size;
                            }
                        }
                    }
                    m_output = bitmap;
                }
            }
        }
    }
}
