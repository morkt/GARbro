//! \file       ImageTGA.cs
//! \date       Fri Jul 04 07:24:38 2014
//! \brief      Targa image implementation.
//
// Copyright (C) 2014-2015 by morkt
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
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel.Composition;

namespace GameRes
{
    public class TgaMetaData : ImageMetaData
    {
        public short    ImageType;
        public short    ColormapType;
        public uint     ColormapOffset;
        public ushort   ColormapFirst;
        public ushort   ColormapLength;
        public short    ColormapDepth;
        public short    Descriptor;
    }

    [Export(typeof(ImageFormat))]
    public class TgaFormat : ImageFormat
    {
        public override string         Tag { get { return "TGA"; } }
        public override string Description { get { return "Truevision TGA image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public override ImageData Read (IBinaryStream stream, ImageMetaData metadata)
        {
            var reader = new Reader (stream, (TgaMetaData)metadata);
            var pixels = reader.Unpack();
            return ImageData.Create (metadata, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream stream, ImageData image)
        {
            using (var file = new BinaryWriter (stream, System.Text.Encoding.ASCII, true))
            {
                file.Write ((byte)0);   // idlength
                file.Write ((byte)0);   // colourmaptype
                file.Write ((byte)2);   // datatypecode
                file.Write ((short)0);  // colourmaporigin
                file.Write ((short)0);  // colourmaplength
                file.Write ((byte)0);   // colourmapdepth
                file.Write ((short)image.OffsetX);
                file.Write ((short)image.OffsetY);
                file.Write ((ushort)image.Width);
                file.Write ((ushort)image.Height);

                var bitmap = image.Bitmap;
                int bpp = 0;
                int stride = 0;
                byte descriptor = 0;
                if (PixelFormats.Bgr24 == bitmap.Format)
                {
                    bpp = 24;
                    stride = (int)image.Width*3;
                }
                else if (PixelFormats.Bgr32 == bitmap.Format)
                {
                    bpp = 32;
                    stride = (int)image.Width*4;
                }
                else
                {
                    bpp = 32;
                    stride = (int)image.Width*4;
                    if (PixelFormats.Bgra32 != bitmap.Format)
                    {
                        var converted_bitmap = new FormatConvertedBitmap();
                        converted_bitmap.BeginInit();
                        converted_bitmap.Source = image.Bitmap;
                        converted_bitmap.DestinationFormat = PixelFormats.Bgra32;
                        converted_bitmap.EndInit();
                        bitmap = converted_bitmap;
                    }
                }
                file.Write ((byte)bpp);
                file.Write (descriptor);
                byte[] row_data = new byte[stride];
                Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
                for (uint row = 0; row < image.Height; ++row)
                {
                    --rect.Y;
                    bitmap.CopyPixels (rect, row_data, stride, 0);
                    file.Write (row_data);
                }
            }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            short id_length     = (short)file.ReadByte();
            short colormap_type = (short)file.ReadByte();
            if (colormap_type > 1)
                return null;
            short image_type    = (short)file.ReadByte();
            ushort colormap_first  = file.ReadUInt16();
            ushort colormap_length = file.ReadUInt16();
            short colormap_depth  = (short)file.ReadByte();
            int pos_x           = file.ReadInt16();
            int pos_y           = file.ReadInt16();
            uint width          = file.ReadUInt16();
            uint height         = file.ReadUInt16();
            int bpp             = file.ReadByte();
            if (bpp != 32 && bpp != 24 && bpp != 16 && bpp != 15 && bpp != 8)
                return null;
            short descriptor    = (short)file.ReadByte();
            uint colormap_offset = (uint)(18 + id_length);
            switch (image_type)
            {
            default: return null;
            case 1:  // Uncompressed, color-mapped images.
            case 9:  // Runlength encoded color-mapped images.
            case 32: // Compressed color-mapped data, using Huffman, Delta, and
                    // runlength encoding.
            case 33: // Compressed color-mapped data, using Huffman, Delta, and
                    // runlength encoding.  4-pass quadtree-type process.
                if (colormap_depth != 24 && colormap_depth != 32)
                    return null;
                break;
            case 2:  // Uncompressed, RGB images.
            case 3:  // Uncompressed, black and white images.
            case 10: // Runlength encoded RGB images.
            case 11: // Compressed, black and white images.
                break;
            }
            return new TgaMetaData {
                OffsetX = pos_x,
                OffsetY = pos_y,
                Width   = width,
                Height  = height,
                BPP     = bpp,
                ImageType       = image_type,
                ColormapType    = colormap_type,
                ColormapOffset  = colormap_offset,
                ColormapFirst   = colormap_first,
                ColormapLength  = colormap_length,
                ColormapDepth   = colormap_depth,
                Descriptor      = descriptor,
            };
        }

        internal class Reader
        {
            IBinaryStream   m_input;
            TgaMetaData     m_meta;
            int             m_width;
            int             m_height;
            int             m_stride;
            byte[]          m_data;
            long            m_image_offset;

            public PixelFormat    Format { get; private set; }
            public BitmapPalette Palette { get; private set; }
            public int            Stride { get { return m_stride; } }
            public byte[]           Data { get { return m_data; } }

            public Reader (IBinaryStream stream, TgaMetaData meta)
            {
                m_input = stream;
                m_meta = meta;
                switch (meta.BPP)
                {
                default: throw new InvalidFormatException();
                case 8:
                    if (1 == meta.ColormapType)
                        Format = PixelFormats.Indexed8;
                    else
                        Format = PixelFormats.Gray8;
                    break;
                case 15: Format = PixelFormats.Bgr555; break;
                case 16: Format = PixelFormats.Bgr555; break;
                case 32: Format = PixelFormats.Bgra32; break;
                case 24:
                    if (8 == (meta.Descriptor & 0xf))
                        Format = PixelFormats.Bgr32;
                    else
                        Format = PixelFormats.Bgr24;
                    break;
                }
                int colormap_size = meta.ColormapLength * meta.ColormapDepth / 8;
                m_width  = (int)meta.Width;
                m_height = (int)meta.Height;
                m_stride = m_width * ((Format.BitsPerPixel+7) / 8);
                m_image_offset = meta.ColormapOffset;
                if (1 == meta.ColormapType)
                {
                    m_image_offset += colormap_size;
                    m_input.Position = meta.ColormapOffset;
                    ReadColormap (meta.ColormapLength, meta.ColormapDepth);
                }
                m_data = new byte[m_stride*m_height];
            }

            private void ReadColormap (int length, int depth)
            {
                if (24 != depth && 32 != depth)
                    throw new NotImplementedException();
                int pixel_size = depth / 8;
                var palette_data = new byte[length * pixel_size];
                if (palette_data.Length != m_input.Read (palette_data, 0, palette_data.Length))
                    throw new InvalidFormatException();

                var palette = new Color[length];
                for (int i = 0; i < palette.Length; ++i)
                {
                    byte b = palette_data[i*pixel_size];
                    byte g = palette_data[i*pixel_size+1];
                    byte r = palette_data[i*pixel_size+2];
                    palette[i] = Color.FromRgb (r, g, b);
                }
                Palette = new BitmapPalette (palette);
            }

            public byte[] Unpack ()
            {
                switch (m_meta.ImageType)
                {
                case 9:  // Runlength encoded color-mapped images.
                case 32: // Compressed color-mapped data, using Huffman, Delta, and
                        // runlength encoding.
                case 33: // Compressed color-mapped data, using Huffman, Delta, and
                        // runlength encoding.  4-pass quadtree-type process.
                    throw new NotImplementedException();
                default:
                    throw new InvalidFormatException();
                case 1:  // Uncompressed, color-mapped images.
                case 2:  // Uncompressed, RGB images.
                case 3:  // Uncompressed, black and white images.
                    ReadRaw();
                    break;
                case 10: // Runlength encoded RGB images.
                case 11: // Compressed, black and white images.
                    ReadRLE ((m_meta.BPP+7)/8);
                    break;
                }
                return Data;
            }

            void ReadRaw ()
            {
                m_input.Position = m_image_offset;
                if (0 != (m_meta.Descriptor & 0x20))
                {
                    if (m_data.Length != m_input.Read (m_data, 0, m_data.Length))
                        throw new InvalidFormatException();
                }
                else
                {
                    for (int row = m_height-1; row >= 0; --row)
                    {
                        if (m_stride != m_input.Read (m_data, row*m_stride, m_stride))
                            throw new InvalidFormatException();
                    }
                }
            }

            void ReadRLE (int pixel_size)
            {
                m_input.Position = m_image_offset;
                for (int dst = 0; dst < m_data.Length;)
                {
                    int packet = m_input.ReadByte();
                    if (-1 == packet)
                        break;
                    int count = (packet & 0x7f) + 1;
                    if (0 != (packet & 0x80))
                    {
                        if (pixel_size != m_input.Read (m_data, dst, pixel_size))
                            break;
                        int src = dst;
                        dst += pixel_size;
                        for (int i = 1; i < count && dst < m_data.Length; ++i)
                        {
                            Buffer.BlockCopy (m_data, src, m_data, dst, pixel_size);
                            dst += pixel_size;
                        }
                    }
                    else
                    {
                        count *= pixel_size;
                        if (count != m_input.Read (m_data, dst, count))
                            break;
                        dst += count;
                    }
                }
                if (0 == (m_meta.Descriptor & 0x20))
                {
                    byte[] flipped = new byte[m_stride*m_height];
                    int dst = 0;
                    for (int src = m_stride*(m_height-1); src >= 0; src -= m_stride)
                    {
                        Buffer.BlockCopy (m_data, src, flipped, dst, m_stride);
                        dst += m_stride;
                    }
                    m_data = flipped;
                }
            }
        }
    }
}
