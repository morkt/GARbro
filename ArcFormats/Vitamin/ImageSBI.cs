//! \file       ImageSBI.cs
//! \date       Sun Jun 05 23:43:49 2016
//! \brief      Vitamin image format.
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

namespace GameRes.Formats.Vitamin
{
    internal class SbiMetaData : ImageMetaData
    {
        public bool HasPalette;
        public bool IsPacked;
        public int  InputSize;
    }

    [Export(typeof(ImageFormat))]
    public class SbiFormat : ImageFormat
    {
        public override string         Tag { get { return "SBI"; } }
        public override string Description { get { return "Vitamin image format"; } }
        public override uint     Signature { get { return 0x0A494253; } } // 'SBI'

        public SbiFormat ()
        {
            Extensions = new string[] { "cmp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x20);
            if (header[4] != 1 || header[5] != 0)
                return null;
            int bpp = header[6];
            if (bpp < 8)
                return null;
            return new SbiMetaData
            {
                Width   = header.ToUInt16 (7),
                Height  = header.ToUInt16 (9),
                BPP     = bpp,
                InputSize = header.ToInt32 (0xB),
                IsPacked = 0 != header[0x10],
                HasPalette = 8 == bpp && 0 == header[0xF],
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new SbiReader (stream.AsStream, (SbiMetaData)info))
            {
                reader.Unpack();
                return ImageData.Create (info, reader.Format, reader.Palette, reader.Data, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SbiFormat.Write not implemented");
        }
    }

    internal sealed class SbiReader : IDisposable
    {
        BinaryReader    m_input;
        SbiMetaData     m_info;
        byte[]          m_output;
        int             m_stride;

        public byte[]           Data { get { return m_output; } }
        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public int            Stride { get { return m_stride; } }

        public SbiReader (Stream input, SbiMetaData info)
        {
            m_input = new ArcView.Reader (input);
            m_info = info;
            m_stride = ((int)m_info.Width * m_info.BPP / 8 + 3) & ~3;
            m_output = new byte[m_stride * (int)m_info.Height];
            if (32 == m_info.BPP)
                Format = PixelFormats.Bgr32;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else if (16 == m_info.BPP)
                Format = PixelFormats.Bgr565;
            else if (8 == m_info.BPP)
            {
                if (m_info.HasPalette)
                    Format = PixelFormats.Indexed8;
                else
                    Format = PixelFormats.Gray8;
            }
            else
                throw new InvalidFormatException();
        }

        public void Unpack ()
        {
            m_input.BaseStream.Position = 0x20;
            int input_size = m_info.InputSize - 0x20;
            if (m_info.HasPalette)
            {
                ReadPalette();
                input_size -= 0x300;
            }
            int x = 0;
            int y = (int)m_info.Height - 1;
            int dst = m_stride * y;
            if (!m_info.IsPacked)
            {
                while (dst >= 0)
                {
                    if (m_stride != m_input.Read (m_output, dst, m_stride))
                        throw new EndOfStreamException();
                    dst -= m_stride;
                }
                return;
            }
            int pixel_size = m_info.BPP / 8;
            var buffer = new byte[0x180];
            while (input_size > 0)
            {
                int count = m_input.ReadByte();
                --input_size;
                if (count < 0x80)
                {
                    int c = count * pixel_size;
                    if (c != m_input.Read (buffer, 0, c))
                        throw new EndOfStreamException();
                    input_size -= c;
                }
                else if (count >= 0x80)
                {
                    count &= 0x7F;
                    if (pixel_size != m_input.Read (buffer, 0, pixel_size))
                        throw new EndOfStreamException();
                    input_size -= pixel_size;
                    Binary.CopyOverlapped (buffer, 0, pixel_size, pixel_size * (count-1));
                }
                int src = 0;
                while (count > 0)
                {
                    int line_left = (int)m_info.Width - x;
                    if (count < line_left)
                    {
                        line_left = count;
                        x += count;
                    }
                    else
                    {
                        x = 0;
                    }
                    int chunk = pixel_size * line_left;
                    Buffer.BlockCopy (buffer, src, m_output, dst, chunk);
                    src += chunk;
                    if (0 == x)
                        dst = m_stride * --y;
                    else
                        dst += chunk;
                    count -= line_left;
                }
            }
        }

        void ReadPalette ()
        {
            var palette_data = m_input.ReadBytes (0x300);
            if (palette_data.Length != 0x300)
                throw new EndOfStreamException();
            var palette = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                int c = i * 3;
                palette[i] = Color.FromRgb (palette_data[c], palette_data[c+1], palette_data[c+2]);
            }
            Palette = new BitmapPalette (palette);
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
