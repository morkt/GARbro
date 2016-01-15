//! \file       ImageTYP1.cs
//! \date       Thu Jan 14 18:45:18 2016
//! \brief      AZ system image format implementation.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.AZSys
{
    internal class Typ1MetaData : CpbMetaData
    {
        public bool HasPalette;
    }

    [Export(typeof(ImageFormat))]
    public class Typ1Format : ImageFormat
    {
        public override string         Tag { get { return "CPB/TYP1"; } }
        public override string Description { get { return "AZ system image format"; } }
        public override uint     Signature { get { return 0x31505954; } } // 'TYP1'

        public Typ1Format ()
        {
            Extensions = new string[] { "cpb" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 4;
            int bpp = stream.ReadByte();
            bool has_palette = stream.ReadByte() != 0;
            using (var input = new ArcView.Reader (stream))
            {
                var info = new Typ1MetaData { BPP = bpp, HasPalette = has_palette };
                info.Width  = input.ReadUInt16();
                info.Height = input.ReadUInt16();
                input.ReadInt32();
                info.Channel[0] = input.ReadUInt32();
                info.Channel[1] = input.ReadUInt32();
                info.Channel[2] = input.ReadUInt32();
                info.Channel[3] = input.ReadUInt32();
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (Typ1MetaData)info;
            var reader = new Reader (stream, meta);
            reader.Unpack();
            return ImageData.Create (meta, reader.Format, reader.Palette, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Typ1Format.Write not implemented");
        }

        internal class Reader
        {
            int             m_width;
            int             m_height;
            int             m_bpp;
            int             m_pixel_size;
            Stream          m_input;
            byte[]          m_output;
            uint[]          m_channel;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (Stream input, Typ1MetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_pixel_size = 8 == m_bpp ? 1 : 4;
                m_channel = info.Channel;
                m_output = new byte[m_width * m_height * m_pixel_size];
                if (8 == m_bpp)
                    Format = info.HasPalette ? PixelFormats.Indexed8 : PixelFormats.Gray8;
                else if (24 == m_bpp)
                    Format = PixelFormats.Bgr32;
                else if (32 == m_bpp)
                    Format = PixelFormats.Bgra32;
                else
                    throw new InvalidFormatException ("Invalid CPB color depth");
                m_input = input;
                if (info.HasPalette)
                {
                    m_input.Position = 0x1E;
                    Palette = ReadPalette();
                }
            }

            public BitmapPalette ReadPalette ()
            {
                var palette_data = new byte[0x400];
                if (0x400 != m_input.Read (palette_data, 0, 0x400))
                    throw new InvalidFormatException();
                var palette = new Color[0x100];
                for (int i = 0; i < palette.Length; ++i)
                {
                    int src = i * 4;
                    byte b = palette_data[src++];
                    byte g = palette_data[src++];
                    byte r = palette_data[src++];
                    palette[i] = Color.FromRgb (r, g, b);
                }
                return new BitmapPalette (palette);
            }

            public void Unpack ()
            {
                if (8 == m_bpp)
                    UnpackIndexed();
                else
                    UnpackRGB();
            }

            void UnpackIndexed ()
            {
                if (null == Palette)
                    m_input.Position = 0x22;
                else
                    m_input.Position = 0x422;
                using (var input = new ZLibStream (m_input, CompressionMode.Decompress, true))
                    input.Read (m_output, 0, m_output.Length);
            }

            static byte[] StreamMap  = new byte[] { 3, 2, 1, 0 };
            static byte[] ChannelMap = new byte[] { 3, 0, 1, 2 };

            void UnpackRGB ()
            {
                byte[] channel = new byte[m_width*m_height];
                long start_pos = 0x1E;
                for (int i = 0; i < 4; ++i)
                {
                    if (0 == m_channel[StreamMap[i]])
                        continue;
                    m_input.Position = start_pos + 4; // skip crc32
                    using (var input = new ZLibStream (m_input, CompressionMode.Decompress, true))
                    {
                        int channel_size = input.Read (channel, 0, channel.Length);
                        int dst = ChannelMap[i];
                        for (int j = 0; j < channel_size; ++j)
                        {
                            m_output[dst] = channel[j];
                            dst += m_pixel_size;
                        }
                    }
                    start_pos += m_channel[StreamMap[i]];
                }
            }
        }
    }
}
