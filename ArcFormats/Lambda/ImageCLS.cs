//! \file       ImageCLS.cs
//! \date       2017 Dec 21
//! \brief      Lambda engine image format.
//
// Copyright (C) 2017-2018 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Lambda
{
    internal class ClsMetaData : ImageMetaData
    {
        public int FrameOffset;
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class ClsFormat : ImageFormat
    {
        public override string         Tag { get { return "CLS"; } }
        public override string Description { get { return "Lambda engine image format"; } }
        public override uint     Signature { get { return 0x5F534C43; } } // 'CLS_'

        public ClsFormat ()
        {
            Extensions = new string[] { "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            if (!header.AsciiEqual ("CLS_TEXFILE"))
                return null;

            file.Position = header.ToUInt32 (0x14);
            int frame_offset = file.ReadInt32();
            file.Position = frame_offset+4;
            if (file.ReadUInt16() != 1)
                return null;

            file.Position = frame_offset+0x1C;
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            int x = file.ReadInt32();
            int y = file.ReadInt32();

            file.Position = frame_offset+0x30;
            bool compressed = file.ReadByte() != 0;
            int format = file.ReadByte();
            if (format != 4 && format != 5 && format != 2)
                return null;
            return new ClsMetaData {
                Width = width,
                Height = height,
                OffsetX = x,
                OffsetY = y,
                BPP = 8 * (format - 1),
                FrameOffset = frame_offset,
                IsCompressed = compressed,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new ClsReader (file, (ClsMetaData)info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ClsFormat.Write not implemented");
        }
    }

    internal class ClsReader
    {
        IBinaryStream   m_input;
        byte[]          m_channel;
        int             m_width;
        int             m_height;
        int             m_channels;
        int             m_base_offset;
        int[]           m_rows_sizes;
        bool            m_compressed;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public ClsReader (IBinaryStream input, ClsMetaData info)
        {
            m_input = input;
            m_base_offset = info.FrameOffset;
            m_compressed = info.IsCompressed;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_channels = info.BPP / 8;
            if (1 == m_channels)
                Format = PixelFormats.Indexed8;
            else if (4 == m_channels)
                Format = PixelFormats.Bgra32;
            else
                Format = PixelFormats.Bgr24;
            m_channel = new byte[m_width * m_height];
            m_rows_sizes = new int[m_height];
        }

        static readonly byte[] ChannelOrder = { 2, 1, 0, 3 };

        public byte[] Unpack ()
        {
            SetPosition (0x48);
            var offsets = new int[m_channels];
            for (int i = 0; i < m_channels; ++i)
                offsets[i] = m_input.ReadInt32();
            SetPosition (0x58);
            var sizes = new int[m_channels];
            for (int i = 0; i < m_channels; ++i)
                sizes[i] = m_input.ReadInt32();
            if (1 == m_channels)
            {
                SetPosition (0x68);
                int palette_offset = m_input.ReadInt32();
                int palette_size   = m_input.ReadInt32();
                SetPosition (palette_offset);
                Palette = ImageFormat.ReadPalette (m_input.AsStream, palette_size / 4, PaletteFormat.BgrA);
                SetPosition (offsets[0]);
                UnpackChannel (sizes[0]);
                return m_channel;
            }
            else if (!m_compressed)
            {
                var pixels = new byte[sizes[0]];
                SetPosition (offsets[0]);
                m_input.Read (pixels, 0, pixels.Length);
                return pixels;
            }
            var output = new byte[m_width * m_height * m_channels];
            for (int i = 0; i < m_channels; ++i)
            {
                SetPosition (offsets[i]);
                UnpackChannel (sizes[i]);
                int src = 0;
                for (int dst = ChannelOrder[i]; dst < output.Length; dst += m_channels)
                {
                    output[dst] = m_channel[src++];
                }
            }
            return output;
        }

        void SetPosition (int pos)
        {
            m_input.Position = m_base_offset + pos;
        }

        void UnpackChannel (int size)
        {
            if (!m_compressed)
            {
                ReadV0 (size);
                return;
            }
            int method = Binary.BigEndian (m_input.ReadUInt16());
            if (method > 1)
                throw new InvalidFormatException();
            size -= 2;
            if (0 == method)
                ReadV0 (size);
            else
                ReadV1 (size);
        }

        void ReadV0 (int size)
        {
            int row_width = size / m_height;
            if (row_width == m_width)
            {
                m_input.Read (m_channel, 0, m_channel.Length);
            }
            else
            {
                int dst = 0;
                for (int y = 0; y < m_height; ++y)
                {
                    m_input.Read (m_channel, dst, row_width);
                    dst += m_width;
                }
            }
        }

        void ReadV1 (int size)
        {
            int row_count = 0;
            while (size > 0)
            {
                int chunk_size = Binary.BigEndian (m_input.ReadUInt16());
                if (row_count < m_height)
                {
                    m_rows_sizes[row_count++] = chunk_size;
                }
                size -= chunk_size + 2;
            }
            if (size < 0)
                throw new InvalidFormatException();
            int dst = 0;
            for (int y = 0; y < row_count; ++y)
            {
                int width = m_width;
                int chunk_size = m_rows_sizes[y];
                while (chunk_size --> 0)
                {
                    byte rle = m_input.ReadUInt8();
                    if (rle < 0x81)
                    {
                        int count = rle + 1;
                        width -= count;
                        chunk_size -= count;
                        m_input.Read (m_channel, dst, count);
                        dst += count;
                    }
                    else
                    {
                        int count = 0x101 - rle;
                        width -= count;
                        --chunk_size;
                        byte v = m_input.ReadUInt8();
                        while (count --> 0)
                        {
                            m_channel[dst++] = v;
                        }
                    }
                }
                while (width --> 0)
                    m_channel[dst++] = 0;
            }
        }
    }
}
