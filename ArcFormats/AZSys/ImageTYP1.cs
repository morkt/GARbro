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
        public bool SeparateChannels;
        public uint PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class Typ1Format : ImageFormat
    {
        public override string         Tag { get { return "CPB/TYP1"; } }
        public override string Description { get { return "AZ system image format"; } }
        public override uint     Signature { get { return 0x31505954; } } // 'TYP1'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            int bpp = stream.ReadByte();
            bool has_palette = stream.ReadByte() != 0;
            var info = new Typ1MetaData { BPP = bpp };
            info.Width  = stream.ReadUInt16();
            info.Height = stream.ReadUInt16();
            uint packed_size = stream.ReadUInt32();
            uint palette_size = 8 == bpp ? 0x400u : 0u;
            if (packed_size+palette_size+0xE == stream.Length)
            {
                info.SeparateChannels = false;
                info.HasPalette = palette_size > 0;
                info.PackedSize = packed_size;
            }
            else
            {
                info.SeparateChannels = true;
                info.HasPalette = has_palette;
                info.Channel[0] = stream.ReadUInt32();
                info.Channel[1] = stream.ReadUInt32();
                info.Channel[2] = stream.ReadUInt32();
                info.Channel[3] = stream.ReadUInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (Typ1MetaData)info;
            var reader = new Reader (stream.AsStream, meta);
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
            int             m_pixel_size;
            Stream          m_input;
            byte[]          m_output;
            Typ1MetaData    m_info;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (Stream input, Typ1MetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_info = info;
                m_pixel_size = 8 == m_info.BPP ? 1 : 4;
                m_output = new byte[m_width * m_height * m_pixel_size];
                if (8 == m_info.BPP)
                    Format = m_info.HasPalette ? PixelFormats.Indexed8 : PixelFormats.Gray8;
                else if (24 == m_info.BPP)
                    Format = PixelFormats.Bgr32;
                else if (32 == m_info.BPP)
                    Format = PixelFormats.Bgra32;
                else
                    throw new InvalidFormatException ("Invalid CPB color depth");
                m_input = input;
            }

            public void Unpack ()
            {
                if (m_info.HasPalette)
                {
                    m_input.Position = m_info.SeparateChannels ? 0x1E : 0x0E;
                    Palette = ImageFormat.ReadPalette (m_input);
                }
                if (!m_info.SeparateChannels)
                {
                    m_input.Position = m_info.HasPalette ? 0x40E : 0xE;
                    using (var z = new ZLibStream (m_input, CompressionMode.Decompress, true))
                        z.Read (m_output, 0, m_output.Length);
                }
                else if (8 == m_info.BPP)
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
                    if (0 == m_info.Channel[StreamMap[i]])
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
                    start_pos += m_info.Channel[StreamMap[i]];
                }
            }
        }
    }
}
