//! \file       ImageBSG.cs
//! \date       Sat Oct 24 17:07:43 2015
//! \brief      Bishop graphics image.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Bishop
{
    internal class BsgMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public int  ColorMode;
        public int  CompressionMode;
        public int  DataOffset;
        public int  DataSize;
        public int  PaletteOffset;
    }

    [Export(typeof(ImageFormat))]
    public class BsgFormat : ImageFormat
    {
        public override string         Tag { get { return "BSG"; } }
        public override string Description { get { return "Bishop image format"; } }
        public override uint     Signature { get { return 0x2D535342; } } // 'BSS-'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x60];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int base_offset = 0;
            if (Binary.AsciiEqual (header, 0, "BSS-Composition\0"))
                base_offset = 0x20;
            if (!Binary.AsciiEqual (header, base_offset, "BSS-Graphics\0"))
                return null;
            int type = header[base_offset+0x30];
            if (type > 2)
                return null;
            return new BsgMetaData
            {
                Width       = LittleEndian.ToUInt16 (header, base_offset+0x16),
                Height      = LittleEndian.ToUInt16 (header, base_offset+0x18),
                OffsetX     = LittleEndian.ToInt16 (header, base_offset+0x20),
                OffsetY     = LittleEndian.ToInt16 (header, base_offset+0x22),
                UnpackedSize = LittleEndian.ToInt32 (header, base_offset+0x12),
                BPP = 2 == type ? 8 : 32,
                ColorMode   = type,
                CompressionMode = header[base_offset+0x31],
                DataOffset  = LittleEndian.ToInt32 (header, base_offset+0x32)+base_offset,
                DataSize    = LittleEndian.ToInt32 (header, base_offset+0x36),
                PaletteOffset = LittleEndian.ToInt32 (header, base_offset+0x3A)+base_offset,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (BsgMetaData)info;
            using (var reader = new BsgReader (stream, meta))
            {
                reader.Unpack();
                return ImageData.CreateFlipped (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("xxxFormat.Write not implemented");
        }
    }

    internal sealed class BsgReader : IDisposable
    {
        BinaryReader        m_input;
        BsgMetaData         m_info;
        byte[]              m_output;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get; private set; }

        public BsgReader (Stream input, BsgMetaData info)
        {
            m_info = info;
            if (m_info.CompressionMode > 2)
                throw new NotSupportedException ("Not supported BSS Graphics compression");

            m_input = new ArcView.Reader (input);
            m_output = new byte[m_info.UnpackedSize];
            switch (m_info.ColorMode)
            {
            case 0:
                Format = PixelFormats.Bgra32;
                Stride = (int)m_info.Width * 4;
                break;
            case 1:
                Format = PixelFormats.Bgr32;
                Stride = (int)m_info.Width * 4;
                break;
            case 2:
                Format = PixelFormats.Indexed8;
                Stride = (int)m_info.Width;
                Palette = ReadPalette();
                break;
            }
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = m_info.DataOffset;
            if (0 == m_info.CompressionMode)
            {
                if (1 == m_info.ColorMode)
                {
                    int dst = 0;
                    for (int count = m_info.DataSize / 3; count > 0; --count)
                    {
                        m_input.Read (m_output, dst, 3);
                        dst += 4;
                    }
                }
                else
                {
                    m_input.Read (m_output, 0, m_info.DataSize);
                }
            }
            else
            {
                Action<int, int> unpacker;
                if (1 == m_info.CompressionMode)
                    unpacker = UnpackRle;
                else
                    unpacker = UnpackLz;
                if (0 == m_info.ColorMode)
                {
                    for (int channel = 0; channel < 4; ++channel)
                        unpacker (channel, 4);
                }
                else if (1 == m_info.ColorMode)
                {
                    for (int channel = 0; channel < 3; ++channel)
                        unpacker (channel, 4);
                }
                else
                {
                    unpacker (0, 1);
                }
            }
        }

        void UnpackRle (int dst, int pixel_size)
        {
            int remaining = m_input.ReadInt32();
            while (remaining > 0)
            {
                int count = m_input.ReadSByte();
                --remaining;
                if (count >= 0)
                {
                    for (int i = 0; i <= count; ++i)
                    {
                        m_output[dst] = m_input.ReadByte();
                        --remaining;
                        dst += pixel_size;
                    }
                }
                else
                {
                    count = 1 - count;
                    byte repeat = m_input.ReadByte();
                    --remaining;
                    for (int i = 0; i < count; ++i)
                    {
                        m_output[dst] = repeat;
                        dst += pixel_size;
                    }
                }
            }
        }

        void UnpackLz (int plane, int pixel_size)
        {
            int dst = plane;
            byte control = m_input.ReadByte();
            int remaining = m_input.ReadInt32() - 5;
            while (remaining > 0)
            {
                byte c = m_input.ReadByte();
                --remaining;

                if (c == control)
                {
                    int offset = m_input.ReadByte();
                    --remaining;
                    if (offset != control)
                    {
                        int count = m_input.ReadByte();
                        --remaining;

                        if (offset > control)
                            --offset;

                        offset *= pixel_size;

                        while (count --> 0)
                        {
                            m_output[dst] = m_output[dst-offset];
                            dst += pixel_size;
                        }
                        continue;
                    }
                }
                m_output[dst] = c;
                dst += pixel_size;
            }
            for (int i = plane + pixel_size; i < m_output.Length; i += pixel_size)
                m_output[i] += m_output[i-pixel_size];
        }

        BitmapPalette ReadPalette ()
        {
            m_input.BaseStream.Position = m_info.PaletteOffset;
            var palette_data = new byte[0x400];
            if (palette_data.Length != m_input.Read (palette_data, 0, palette_data.Length))
                throw new InvalidFormatException();
            var palette = new Color[0x100];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return new BitmapPalette (palette);
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
