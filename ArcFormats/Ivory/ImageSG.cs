//! \file       ImageSG.cs
//! \date       Mon Sep 12 14:37:19 2016
//! \brief      'fSG' image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ivory
{
    internal class SgMetaData : ImageMetaData
    {
        public SgType   Type;
        public int      DataOffset;
        public int      DataSize;
        public int      RgbMode;
        public uint     JpegKey;
    }

    internal enum SgType
    {
        cRGB, cJPG
    }

    [Export(typeof(ImageFormat))]
    public class SgFormat : ImageFormat
    {
        public override string         Tag { get { return "SG"; } }
        public override string Description { get { return "Ivory image format"; } }
        public override uint     Signature { get { return 0x20475366; } } // 'fSG '

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 8;
            var header = new byte[0x24];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int header_size = LittleEndian.ToInt32 (header, 8);
            if (Binary.AsciiEqual (header, "cRGB"))
                return new SgMetaData
                {
                    Type    = SgType.cRGB,
                    Width   = LittleEndian.ToUInt16 (header, 0x1C),
                    Height  = LittleEndian.ToUInt16 (header, 0x1E),
                    BPP     = LittleEndian.ToUInt16 (header, 0x22),
                    OffsetX = LittleEndian.ToInt16 (header, 0x18),
                    OffsetY = LittleEndian.ToInt16 (header, 0x1A),
                    RgbMode = LittleEndian.ToUInt16 (header, 0x10),
                    DataOffset = 8 + header_size,
                    DataSize = LittleEndian.ToInt32 (header, 0xC),
                };
            else if (Binary.AsciiEqual (header, "cJPG"))
                return new SgMetaData
                {
                    Type    = SgType.cJPG,
                    Width   = LittleEndian.ToUInt16 (header, 0x18),
                    Height  = LittleEndian.ToUInt16 (header, 0x1A),
                    BPP     = 24,
                    OffsetX = LittleEndian.ToInt16 (header, 0x14),
                    OffsetY = LittleEndian.ToInt16 (header, 0x16),
                    DataOffset = 8 + header_size,
                    DataSize = LittleEndian.ToInt32 (header, 0xC),
                    JpegKey = LittleEndian.ToUInt32 (header, 0x20),
                };
            else
                return null;
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (SgMetaData)info;
            if (SgType.cRGB == meta.Type)
            {
                using (var reader = new SgRgbReader (stream, meta))
                {
                    reader.Unpack();
                    return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
                }
            }
            else
            {
                return ReadJpeg (stream, meta);
            }
        }

        ImageData ReadJpeg (Stream stream, SgMetaData info)
        {
            stream.Position = info.DataOffset;
            var input = new byte[info.DataSize];
            if (input.Length != stream.Read (input, 0, input.Length))
                throw new EndOfStreamException();
            PakOpener.Decrypt (input, info.JpegKey);
            using (var img = new MemoryStream (input))
            {
                var decoder = new JpegBitmapDecoder (img, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SgFormat.Write not implemented");
        }
    }

    internal sealed class SgRgbReader : IDisposable
    {
        BinaryReader    m_input;
        SgMetaData      m_info;
        int             m_width;
        int             m_height;
        int             m_stride;
        int             m_channels;
        byte[]          m_output;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]           Data { get { return m_output; } }
        public int            Stride { get { return m_stride; } }

        public SgRgbReader (Stream input, SgMetaData info)
        {
            if (info.Type != SgType.cRGB || !(0x18 == info.BPP || 0x20 == info.BPP))
                throw new InvalidFormatException();
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_width = (int)info.Width;
            m_height = (int)info.Height;
            m_stride = m_width * 4;
            m_channels = m_info.BPP / 8;
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = m_info.DataOffset;
            switch (m_info.RgbMode)
            {
            case 0: UnpackV0(); break;
            case 1: UnpackV1(); break;
            case 2:
                if (4 == m_channels)
                    UnpackV2Alpha();
                else
                    UnpackV2();
                break;
            default:
                throw new NotImplementedException (string.Format ("sRGB image type {0} not implemented", m_info.RgbMode));
            }
        }

        void UnpackV1 ()
        {
            Format = 3 == m_channels ? PixelFormats.Bgr32 : PixelFormats.Bgra32;
            m_output = new byte[m_stride * m_height];
            var line = new byte[m_stride];

            int dst = 0;
            int alpha_pos = m_width * 3;
            for (int y = 0; y < m_height; ++y)
            {
                int line_pos = 0;
                for (int c = 0; c < m_channels; ++c)
                {
                    for (int x = 0; x < m_width; )
                    {
                        byte ctl = m_input.ReadByte();
                        int count = ctl & 0x3F;
                        if (0 != (ctl & 0x40))
                        {
                            count <<= 8;
                            count |= m_input.ReadByte();
                        }
                        if (0 != (ctl & 0x80))
                        {
                            byte v = m_input.ReadByte();
                            for (int i = 0; i < count; ++i)
                                line[line_pos++] = v;
                        }
                        else
                        {
                            m_input.Read (line, line_pos, count);
                            line_pos += count;
                        }
                        x += count;
                    }
                }
                line_pos = 0;
                for (int x = 0; x < m_width; ++x)
                {
                    m_output[dst  ] = line[line_pos];
                    m_output[dst+1] = line[line_pos+m_width];
                    m_output[dst+2] = line[line_pos+m_width*2];
                    if (4 == m_channels)
                    {
                        m_output[dst+3] = line[line_pos+alpha_pos];
                    }
                    dst += 4;
                    ++line_pos;
                }
            }
        }

        void UnpackV0 ()
        {
            Format = 3 == m_channels ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            m_stride = m_width * m_channels;
            m_output = m_input.ReadBytes (m_stride * m_height);
        }

        void UnpackV2 ()
        {
            m_stride = m_width;
            m_output = new byte[m_width * m_height];
            Format = PixelFormats.Indexed8;

            Palette = new BitmapPalette (ReadPalette());
            var index = new int[m_height];
            for (int i = 0; i < m_height; ++i)
                index[i] = m_input.ReadInt32();
            var data_pos = m_input.BaseStream.Position;
            int dst = 0;
            for (int y = 0; y < m_height; ++y)
            {
                m_input.BaseStream.Position = data_pos + index[y];
                for (int x = 0; x < m_width; )
                {
                    int ctl = m_input.ReadByte();
                    int count = ctl >> 2;
                    if (0 != (ctl & 2))
                    {
                        count |= m_input.ReadByte() << 6;
                    }
                    count = Math.Min (count, m_width - x);
                    x += count;
                    if (0 != (ctl & 1))
                    {
                        byte c = m_input.ReadByte();
                        for (int i = 0; i < count; ++i)
                            m_output[dst++] = c;
                    }
                    else
                    {
                        m_input.Read (m_output, dst, count);
                        dst += count;
                    }
                }
            }
        }

        void UnpackV2Alpha ()
        {
            m_output = new byte[m_stride * m_height];
            Format = PixelFormats.Bgra32;

            var palette = ReadPalette();
            var index = new int[m_height];
            for (int i = 0; i < m_height; ++i)
                index[i] = m_input.ReadInt32();
            var data_pos = m_input.BaseStream.Position;
            int dst = 0;
            using (var bits = new LsbBitStream (m_input.BaseStream, true))
            {
                for (int y = 0; y < m_height; ++y)
                {
                    bits.Input.Position = data_pos + index[y];
                    bits.Reset();
                    for (int x = 0; x < m_width; )
                    {
                        int ctl = bits.GetBits (2);
                        int count;
                        if (0 != (ctl & 2))
                        {
                            count = bits.GetBits (10);
                        }
                        else
                        {
                            count = bits.GetBits (2);
                        }
                        count = Math.Min (count, m_width - x);
                        x += count;
                        if (0 != (ctl & 1))
                        {
                            byte a = (byte)bits.GetBits (4);
                            if (a != 0)
                                a = (byte)((a << 4) | 0xF);
                            int c = bits.GetBits (8);
                            if (-1 == c)
                                throw new EndOfStreamException();
                            var color = palette[c];
                            for (int i = 0; i < count; ++i)
                            {
                                m_output[dst++] = color.B;
                                m_output[dst++] = color.G;
                                m_output[dst++] = color.R;
                                m_output[dst++] = a;
                            }
                        }
                        else
                        {
                            for (int i = 0; i < count; ++i)
                            {
                                int a = bits.GetBits (4);
                                if (a != 0)
                                    a = (a << 4) | 0xF;
                                int c = bits.GetBits (8);
                                if (-1 == c)
                                    throw new EndOfStreamException();
                                var color = palette[c];
                                m_output[dst++] = color.B;
                                m_output[dst++] = color.G;
                                m_output[dst++] = color.R;
                                m_output[dst++] = (byte)a;
                            }
                        }
                    }
                }
            }
        }

        Color[] ReadPalette ()
        {
            var palette_data = m_input.ReadBytes (0x400);
            if (palette_data.Length != 0x400)
                throw new EndOfStreamException();
            var palette = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return palette;
        }

        #region IDisposable Members
        bool _disposed = false;
        public void Dispose ()
        {
            if (!_disposed)
            {
                m_input.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
}
