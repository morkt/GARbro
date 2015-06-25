//! \file       ImageGRP.cs
//! \date       Wed Jun 24 22:14:41 2015
//! \brief      Cherry Soft compressed image format.
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Cherry
{
    internal class GrpMetaData : ImageMetaData
    {
        public int PackedSize;
        public int UnpackedSize;
        public int Offset;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP/CHERRY"; } }
        public override string Description { get { return "Cherry Soft comprressed image format"; } }
        public override uint     Signature { get { return 0; } }

        public GrpFormat ()
        {
            Extensions = new string[] { "grp" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x18];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            uint width  = LittleEndian.ToUInt32 (header, 0);
            uint height = LittleEndian.ToUInt32 (header, 4);
            int bpp = LittleEndian.ToInt32 (header, 8);
            int packed_size = LittleEndian.ToInt32 (header, 0x0C);
            int unpacked_size = LittleEndian.ToInt32 (header, 0x10);
            if (0 == width || 0 == height || width > 0x7fff || height > 0x7fff
                || (bpp != 24 && bpp != 8)
                || unpacked_size <= 0 || packed_size < 0)
                return null;
            return new GrpMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
                PackedSize = packed_size,
                UnpackedSize = unpacked_size,
                Offset = LittleEndian.ToInt32 (header, 0x14),
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as GrpMetaData;
            if (null == meta)
                throw new ArgumentException ("GrpFormat.Read should be supplied with GrpMetaData", "info");
            var reader = new GrpReader (stream, meta);
            return reader.CreateImage();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GrpFormat.Write not implemented");
        }
    }

    internal class GrpReader
    {
        GrpMetaData     m_info;
        Stream          m_input;
        byte[]          m_image_data;
        int             m_stride;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]         Pixels { get { return m_image_data; } }

        public GrpReader (Stream input, GrpMetaData info)
        {
            m_info = info;
            m_input = input;
            if (8 == info.BPP)
                Format = PixelFormats.Indexed8;
            else if (24 == info.BPP)
                Format = PixelFormats.Bgr24;
            else
                throw new NotSupportedException ("Not supported GRP image depth");
            m_stride = (int)m_info.Width*((Format.BitsPerPixel+7)/8);
        }

        public ImageData CreateImage ()
        {
            m_input.Position = 0x18;
            int data_size = m_info.UnpackedSize;
            if (m_info.PackedSize != 0)
                data_size = m_info.PackedSize;

            if (0x0f0f0f0f == m_info.Offset && 0x18 + data_size == m_input.Length)
                return ReadV2();
            else if (8  == m_info.BPP && 0x418 == m_info.Offset ||
                     24 == m_info.BPP && 0x018 == m_info.Offset)
                return ReadV1();
            else
                throw new InvalidFormatException();
        }

        private ImageData ReadV1 ()
        {
            if (8 == m_info.BPP)
            {
                var palette_data = new byte[0x400];
                if (palette_data.Length != m_input.Read (palette_data, 0, palette_data.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
                SetPalette (palette_data);
            }
            var packed = new byte[m_info.PackedSize];
            if (packed.Length != m_input.Read (packed, 0, packed.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
            for (int i = 0; i < packed.Length; ++i)
                packed[i] ^= (byte)i;

            using (var input = new MemoryStream (packed))
            using (var reader = new LzssReader (input, packed.Length, m_info.UnpackedSize))
            {
                reader.Unpack();
                m_image_data = new byte[m_info.UnpackedSize];
                // flip pixels vertically
                int dst = 0;
                for (int src = m_stride * ((int)m_info.Height-1); src >= 0; src -= m_stride)
                {
                    Buffer.BlockCopy (reader.Data, src, m_image_data, dst, m_stride);
                    dst += m_stride;
                }
            }
            return ImageData.Create (m_info, Format, Palette, m_image_data);
        }

        private ImageData ReadV2 ()
        {
            if (0 != m_info.PackedSize)
            {
                using (var reader = new LzssReader (m_input, m_info.PackedSize, m_info.UnpackedSize))
                {
                    reader.Unpack();
                    m_image_data = reader.Data;
                }
            }
            else
            {
                m_image_data = new byte[m_info.UnpackedSize];
                if (m_image_data.Length != m_input.Read (m_image_data, 0, m_image_data.Length))
                    throw new InvalidFormatException ("Unexpected end of file");
            }
            int pixels_offset = 0;
            if (8 == m_info.BPP)
            {
                SetPalette (m_image_data);
                pixels_offset += 0x400;
            }

            if (0 == pixels_offset)
                return ImageData.Create (m_info, Format, Palette, m_image_data);

            if (pixels_offset + m_stride*(int)m_info.Height > m_image_data.Length)
                throw new InvalidFormatException();
            unsafe
            {
                fixed (byte* pixels = &m_image_data[pixels_offset])
                {
                    var bitmap = BitmapSource.Create ((int)m_info.Width, (int)m_info.Height,
                        ImageData.DefaultDpiX, ImageData.DefaultDpiY, Format, Palette,
                        (IntPtr)pixels, m_image_data.Length-pixels_offset, m_stride);
                    bitmap.Freeze();
                    return new ImageData (bitmap, m_info);
                }
            }
        }

        private void SetPalette (byte[] palette_data)
        {
            if (palette_data.Length < 0x400)
                throw new InvalidFormatException();

            var palette = new Color[0x100];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            Palette = new BitmapPalette (palette);
        }
    }
}
