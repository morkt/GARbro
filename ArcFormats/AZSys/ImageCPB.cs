//! \file       ImageCPB.cs
//! \date       Wed Apr 22 11:08:13 2015
//! \brief      AZ system image format implementation.
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
using GameRes.Compression;

namespace GameRes.Formats.AZSys
{
    internal class CpbMetaData : ImageMetaData
    {
        public uint[] Channel = new uint[4];
    }

    [Export(typeof(ImageFormat))]
    public class CpbFormat : ImageFormat
    {
        public override string         Tag { get { return "CPB"; } }
        public override string Description { get { return "AZ system image format"; } }
        public override uint     Signature { get { return 0x1a425043; } } // 'CPB\x1a'

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CpbFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Seek (5, SeekOrigin.Current);
            int bpp = stream.ReadByte();
            if (24 != bpp && 32 != bpp)
                throw new NotSupportedException ("Not supported CPB image format");
            using (var input = new ArcView.Reader (stream))
            {
                int version = input.ReadInt16 ();
                if (1 != version)
                    throw new NotSupportedException ("Not supported CPB image version");
                var info = new CpbMetaData ();
                info.BPP = bpp;
                input.ReadUInt32();
                info.Width  = input.ReadUInt16();
                info.Height = input.ReadUInt16();
                info.Channel[0] = input.ReadUInt32(); // Alpha
                info.Channel[1] = input.ReadUInt32();
                info.Channel[2] = input.ReadUInt32();
                info.Channel[3] = input.ReadUInt32();
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as CpbMetaData;
            if (null == meta)
                throw new ArgumentException ("CpbFormat.Read should be supplied with CpbMetaData", "info");

            stream.Position = 0x20;
            var reader = new Reader (stream, meta);
            reader.Unpack();
            return ImageData.Create (meta, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            int             m_width;
            int             m_height;
            int             m_bpp;
            Stream          m_input;
            byte[]          m_output;
            uint[]          m_channel;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (Stream input, CpbMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_bpp = info.BPP;
                m_channel = info.Channel;
                m_output = new byte[m_width * m_height * 4];
                if (8 == m_bpp)
                    Format = PixelFormats.Indexed8;
                else if (24 == m_bpp)
                    Format = PixelFormats.Bgr32;
                else
                    Format = PixelFormats.Bgra32;
                m_input = input;
            }

            static byte[] StreamMap  = new byte[] { 0, 3, 1, 2 };
            static byte[] ChannelMap = new byte[] { 3, 0, 1, 2 };

            public void Unpack ()
            {
                byte[] channel = new byte[m_width*m_height];
                long start_pos = m_input.Position;
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
                            dst += 4;
                        }
                    }
                    start_pos += m_channel[StreamMap[i]];
                }
            }
        }
    }
}
