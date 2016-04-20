//! \file       ImageCPB.cs
//! \date       Wed Apr 22 11:08:13 2015
//! \brief      AZ system image format implementation.
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AZSys
{
    internal class CpbMetaData : ImageMetaData
    {
        public int      Type;
        public int      Version;
        public uint[]   Channel = new uint[4];
        public uint     DataOffset;
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
            stream.Seek (4, SeekOrigin.Current);
            int type = stream.ReadByte();
            int bpp = stream.ReadByte();
            if (24 != bpp && 32 != bpp)
                throw new NotSupportedException ("Not supported CPB image format");
            using (var input = new ArcView.Reader (stream))
            {
                int version = input.ReadInt16 ();
                if (1 != version && 0 != version)
                    throw new NotSupportedException ("Not supported CPB image version");
                var info = new CpbMetaData {
                    Type = type,
                    Version = version,
                    BPP = bpp,
                };
                if (1 == version)
                {
                    input.ReadUInt32();
                    info.Width  = input.ReadUInt16();
                    info.Height = input.ReadUInt16();
                    info.Channel[0] = input.ReadUInt32();
                    info.Channel[1] = input.ReadUInt32();
                    info.Channel[2] = input.ReadUInt32();
                    info.Channel[3] = input.ReadUInt32();
                }
                else
                {
                    info.Width  = input.ReadUInt16();
                    info.Height = input.ReadUInt16();
                    input.ReadUInt32();
                    info.Channel[0] = input.ReadUInt32();
                    info.Channel[1] = input.ReadUInt32();
                    info.Channel[2] = input.ReadUInt32();
                    info.Channel[3] = input.ReadUInt32();
                }
                info.DataOffset = (uint)stream.Position;
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var reader = new Reader (stream, (CpbMetaData)info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        internal class Reader
        {
            int             m_width;
            int             m_height;
            int             m_bpp;
            Stream          m_input;
            byte[]          m_output;
            uint[]          m_channel;
            CpbMetaData     m_info;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public byte[]           Data { get { return m_output; } }

            public Reader (Stream input, CpbMetaData info)
            {
                m_width = (int)info.Width;
                m_height = (int)info.Height;
                m_info = info;
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
                m_input.Position = info.DataOffset;

                if (1 == m_info.Version)
                {
                    StreamMap  = new byte[] { 0, 3, 1, 2 };
                    ChannelMap = new byte[] { 3, 0, 1, 2 };
                }
                else
                {
                    StreamMap  = new byte[] { 0, 1, 2, 3 };
                    ChannelMap = new byte[] { 2, 1, 0, 3 };
                }
            }

            byte[] StreamMap;
            byte[] ChannelMap;

            public void Unpack ()
            {
                if (0 == m_info.Version && 3 == m_info.Type)
                    UnpackV3();
                else
                    UnpackV0();
            }

            void UnpackV0 ()
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

            void UnpackV3 ()
            {
                byte[] channel = new byte[m_width*m_height];
                long start_pos = m_input.Position;
                for (int i = 0; i < 4; ++i)
                {
                    int packed_size = (int)m_channel[StreamMap[i]];
                    if (0 == packed_size)
                        continue;
                    m_input.Position = start_pos;
                    int channel_size = Decompress (packed_size, channel);
                    int dst = ChannelMap[i];
                    for (int j = 0; j < channel_size; ++j)
                    {
                        m_output[dst] = channel[j];
                        dst += 4;
                    }
                    start_pos += packed_size;
                }
            }

            int Decompress (int input_size, byte[] output)
            {
                var input = new byte[input_size];
                if (input_size != m_input.Read (input, 0, input_size))
                    throw new EndOfStreamException();
                int src1 = 0x14;
                int src2 = src1 + LittleEndian.ToInt32 (input, 4);
                int src3 = src2 + LittleEndian.ToInt32 (input, 8);
                int remaining = LittleEndian.ToInt32 (input, 0x10);
                int dst = 0;
                int mask = 0x80;
                while (remaining > 0)
                {
                    int count;
                    if (0 != (mask & input[src1]))
                    {
                        int offset = LittleEndian.ToUInt16 (input, src2);
                        src2 += 2;
                        count = (offset >> 13) + 3;
                        offset = (offset & 0x1FFF) + 1;
                        Binary.CopyOverlapped (output, dst-offset, dst, count);
                    }
                    else
                    {
                        count = input[src3++] + 1;
                        Buffer.BlockCopy (input, src3, output, dst, count);
                        src3 += count;
                    }
                    dst += count;
                    remaining -= count;
                    mask >>= 1;
                    if (0 == mask)
                    {
                        ++src1;
                        mask = 0x80;
                    }
                }
                return dst;
            }
        }
    }
}
