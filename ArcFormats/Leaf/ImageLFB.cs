//! \file       ImageLFB.cs
//! \date       2026-09-30
//! \brief      Leaf LFB/LCF/LFF image formats (To Heart PSE).
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Leaf
{
    internal class LfbMetaData : ImageMetaData
    {
        public int OutputSize;
    }

    [Export(typeof(ImageFormat))]
    public class LfbFormat : ImageFormat
    {
        public override string         Tag { get { return "LFB"; } }
        public override string Description { get { return "Leaf LFB image format"; } }
        public override uint     Signature { get { return 0; } }

        public LfbFormat ()
        {
            Extensions = new string[] { "lfb" };
        }

        const int BitmapFileHeaderSize      = 14;
        const int BitmapInfoHeaderSize      = 40;
        const int IndexedAlphaPaletteOffset = BitmapFileHeaderSize + BitmapInfoHeaderSize;
        const int IndexedAlphaPaletteSize   = 256 * 4;
        const int IndexedAlphaPixelOffset   = IndexedAlphaPaletteOffset + IndexedAlphaPaletteSize;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 5)
                return null;
            int output_size = file.ReadInt32 ();
            if (output_size <= 0 || output_size > 0x4000000)
                return null;
            file.Position = 4;
            byte[] bmp_bytes;
            if (!LeafLzs.Decompress (file.ReadBytes ((int)(file.Length-4)), output_size, out bmp_bytes))
                return null;
            if (bmp_bytes.Length < 2 || bmp_bytes[0] != 'B' || bmp_bytes[1] != 'M')
                return null;
            int width, height;
            if (!GetBmpDimensions (bmp_bytes, out width, out height))
                return null;
            return new LfbMetaData {
                Width = (uint)width, Height = (uint)height, BPP = 32,
                OutputSize = output_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LfbMetaData)info;
            file.Position = 4;
            byte[] bmp_bytes;
            if (!LeafLzs.Decompress (file.ReadBytes ((int)(file.Length-4)), meta.OutputSize, out bmp_bytes))
                throw new InvalidFormatException ();
            if (bmp_bytes.Length < 2 || bmp_bytes[0] != 'B' || bmp_bytes[1] != 'M')
                throw new InvalidFormatException ();
            var image = TryDecodeIndexedAlpha (bmp_bytes, meta) ?? TryDecodeLeaf32 (bmp_bytes, meta);
            if (null != image)
                return image;
            using (var bmp_stream = new BinMemoryStream (bmp_bytes, meta.FileName))
            {
                var fallback = ImageFormat.Read (bmp_stream);
                if (null != fallback)
                    return fallback;
            }
            throw new InvalidFormatException ();
        }

        static bool GetBmpDimensions (byte[] bmp, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bmp.Length < 0x1A)
                return false;
            width = GetInt32 (bmp, 18);
            int height_raw = GetInt32 (bmp, 22);
            height = height_raw > 0 ? height_raw : -height_raw;
            return width > 0 && height > 0;
        }

        // 16bpp variant: every pixel is an alpha byte followed by a palette index,
        // the interleaved BGRA palette follows the BITMAPINFOHEADER.
        ImageData TryDecodeIndexedAlpha (byte[] bmp, LfbMetaData meta)
        {
            if (bmp.Length < IndexedAlphaPixelOffset)
                return null;
            int pixel_offset = GetInt32 (bmp, 10);
            int dib_size     = GetInt32 (bmp, 14);
            int width        = GetInt32 (bmp, 18);
            int height_raw   = GetInt32 (bmp, 22);
            int planes       = GetUInt16 (bmp, 26);
            int bpp          = GetUInt16 (bmp, 28);
            int compression  = GetInt32 (bmp, 30);
            if (dib_size != BitmapInfoHeaderSize || planes != 1 || bpp != 16 || compression != 0)
                return null;
            if (pixel_offset != IndexedAlphaPixelOffset || width <= 0 || 0 == height_raw)
                return null;
            int height = height_raw > 0 ? height_raw : -height_raw;
            bool bottom_up = height_raw > 0;
            int src_stride = width*2;
            long pixel_bytes = (long)src_stride * height;
            if (pixel_offset < 0 || pixel_offset + pixel_bytes > bmp.Length)
                return null;

            var palette = new byte[IndexedAlphaPaletteSize];
            Buffer.BlockCopy (bmp, IndexedAlphaPaletteOffset, palette, 0, palette.Length);
            var pixels = new byte[width*height*4];
            for (int y = 0; y < height; ++y)
            {
                int src = pixel_offset + (bottom_up ? height-1-y : y)*src_stride;
                int dst = y*width*4;
                for (int x = 0; x < width; ++x)
                {
                    int pal = bmp[src + x*2 + 1]*4;
                    pixels[dst+x*4  ] = palette[pal  ];
                    pixels[dst+x*4+1] = palette[pal+1];
                    pixels[dst+x*4+2] = palette[pal+2];
                    pixels[dst+x*4+3] = bmp[src + x*2]; // alpha from the high byte
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        // 32bpp variant: pixel data is ABGR, stored bottom-up.
        ImageData TryDecodeLeaf32 (byte[] bmp, LfbMetaData meta)
        {
            if (bmp.Length < BitmapFileHeaderSize + BitmapInfoHeaderSize)
                return null;
            int pixel_offset = GetInt32 (bmp, 10);
            int dib_size     = GetInt32 (bmp, 14);
            int width        = GetInt32 (bmp, 18);
            int height_raw   = GetInt32 (bmp, 22);
            int planes       = GetUInt16 (bmp, 26);
            int bpp          = GetUInt16 (bmp, 28);
            int compression  = GetInt32 (bmp, 30);
            if (dib_size != BitmapInfoHeaderSize || planes != 1 || bpp != 32 || compression != 0)
                return null;
            if (pixel_offset < BitmapFileHeaderSize + BitmapInfoHeaderSize || width <= 0 || 0 == height_raw)
                return null;
            int height = height_raw > 0 ? height_raw : -height_raw;
            bool bottom_up = height_raw > 0;
            int src_stride = width*4;
            long pixel_bytes = (long)src_stride * height;
            if (pixel_offset < 0 || pixel_offset + pixel_bytes > bmp.Length)
                return null;

            var pixels = new byte[width*height*4];
            for (int y = 0; y < height; ++y)
            {
                int src = pixel_offset + (bottom_up ? height-1-y : y)*src_stride;
                int dst = y*width*4;
                for (int x = 0; x < width; ++x)
                {
                    pixels[dst+x*4  ] = bmp[src + x*4 + 1]; // B
                    pixels[dst+x*4+1] = bmp[src + x*4 + 2]; // G
                    pixels[dst+x*4+2] = bmp[src + x*4 + 3]; // R
                    pixels[dst+x*4+3] = bmp[src + x*4];     // A
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        static int GetInt32 (byte[] data, int offset)
        {
            return data[offset] | (data[offset+1] << 8) | (data[offset+2] << 16) | (data[offset+3] << 24);
        }

        static int GetUInt16 (byte[] data, int offset)
        {
            return data[offset] | (data[offset+1] << 8);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("LfbFormat.Write not implemented");
        }
    }

    internal class LcfMetaData : ImageMetaData
    {
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class LcfFormat : ImageFormat
    {
        public override string         Tag { get { return "LCF"; } }
        public override string Description { get { return "Leaf LCF image format"; } }
        public override uint     Signature { get { return 0x4641454C; } } // 'LEAF'

        public LcfFormat ()
        {
            Extensions = new string[] { "lcf" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 24)
                return null;
            var header = file.ReadHeader (24);
            if (header[4] != 'C' || header[5] != 'F' || header[6] != 'L')
                return null;
            if (header.ToUInt32 (0x10) != 24)   // pixel data follows the header immediately
                return null;
            int width  = (int)(header.ToUInt32 (0x0C) & 0xFFFF);
            int height = (int)(header.ToUInt32 (0x0E) & 0xFFFF);
            int unpacked = header.ToInt32 (0x14);
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || unpacked < 0)
                return null;
            return new LcfMetaData {
                Width = (uint)width, Height = (uint)height, BPP = 32,
                UnpackedSize = unpacked,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LcfMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            file.Position = 24;
            byte[] src;
            if (!LeafLzs.Decompress (file.ReadBytes ((int)(file.Length-24)), meta.UnpackedSize, out src))
                throw new InvalidFormatException ();
            // every pixel starts with a control byte: 0 = transparent, otherwise
            // BGR follow with alpha (0xFF = opaque); rows are stored bottom-up.
            var pixels = new byte[width*height*4];
            int src_pos = 0;
            for (int y = 0; y < height; ++y)
            {
                int dst = (height-1-y)*width*4;
                for (int x = 0; x < width; ++x)
                {
                    int d = dst + x*4;
                    if (src_pos >= src.Length)
                        throw new InvalidFormatException ();
                    byte control = src[src_pos++];
                    if (0 == control)
                        continue;   // pixels[] is zero-initialized, stays transparent
                    if (src_pos + 3 > src.Length)
                        throw new InvalidFormatException ();
                    byte b = src[src_pos++];
                    byte g = src[src_pos++];
                    byte r = src[src_pos++];
                    pixels[d  ] = b;
                    pixels[d+1] = g;
                    pixels[d+2] = r;
                    pixels[d+3] = 0xFF == control ? (byte)255 : control;
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("LcfFormat.Write not implemented");
        }
    }

    internal class LffMetaData : ImageMetaData
    {
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class LffFormat : ImageFormat
    {
        public override string         Tag { get { return "LFF"; } }
        public override string Description { get { return "Leaf LFF image format"; } }
        public override uint     Signature { get { return 0x4641454C; } } // 'LEAF'

        public LffFormat ()
        {
            Extensions = new string[] { "lff" };
        }

        const int HeaderSize = 20;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < HeaderSize)
                return null;
            var header = file.ReadHeader (HeaderSize);
            if (header[4] != 'F' || header[5] != 'U' || header[6] != 'L')
                return null;
            if (header.ToInt16 (8) != 0 || header.ToInt16 (10) != 0)
                return null;
            int width  = header.ToInt16 (0x0C);
            int height = header.ToInt16 (0x0E);
            uint data_offset = header.ToUInt32 (0x10);
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
                return null;
            if (data_offset < HeaderSize || data_offset > (ulong)file.Length)
                return null;
            return new LffMetaData {
                Width = (uint)width, Height = (uint)height, BPP = 24,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LffMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            int row_size = width*3;
            file.Position = meta.DataOffset;
            byte[] src;
            if (!LeafLzs.Decompress (file.ReadBytes ((int)(file.Length-meta.DataOffset)),
                                     row_size*height, out src))
                throw new InvalidFormatException ();
            // 24bpp BGR rows, stored bottom-up.
            var pixels = new byte[width*height*4];
            for (int src_y = 0; src_y < height; ++src_y)
            {
                int dst = (height-1-src_y)*width*4;
                for (int x = 0; x < width; ++x)
                {
                    pixels[dst+x*4  ] = src[src_y*row_size + x*3  ]; // B
                    pixels[dst+x*4+1] = src[src_y*row_size + x*3+1]; // G
                    pixels[dst+x*4+2] = src[src_y*row_size + x*3+2]; // R
                    pixels[dst+x*4+3] = 0xFF;
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("LffFormat.Write not implemented");
        }
    }

    // LZSS variant used by LFB/LCF/LFF images: the flag byte, literal bytes and
    // match pairs are all bitwise-inverted, flag bits are consumed MSB-first;
    // ring buffer is 0x1000 bytes, starting at 0xFEE (leafpack.c:leafpack_lzs2).
    internal static class LeafLzs
    {
        public static bool Decompress (byte[] data, int output_size, out byte[] output)
        {
            output = new byte[output_size];
            if (0 == output_size)
                return true;
            var ring = new byte[0x1000];
            int ring_pos = 0xFEE;
            int src = 0;
            int dst = 0;
            while (dst < output_size)
            {
                if (src >= data.Length)
                    return false;
                int flags = ~data[src++] & 0xFF;    // inverted: set bit = literal
                for (int bit = 0x80; bit != 0 && dst < output_size; bit >>= 1)
                {
                    if (0 != (flags & bit))
                    {
                        if (src >= data.Length)
                            return false;
                        byte value = (byte)~data[src++];
                        output[dst] = value;
                        ring[ring_pos] = value;
                        ring_pos = (ring_pos+1) & 0xFFF;
                        dst++;
                    }
                    else
                    {
                        if (src+2 > data.Length)
                            return false;
                        int pair = ~(data[src] | (data[src+1] << 8)) & 0xFFFF;
                        src += 2;
                        int offset = pair >> 4;
                        int count  = (pair & 0x0F) + 3;
                        for (int i = 0; i < count && dst < output_size; ++i)
                        {
                            byte value = ring[(offset+i) & 0xFFF];
                            output[dst] = value;
                            ring[ring_pos] = value;
                            ring_pos = (ring_pos+1) & 0xFFF;
                            dst++;
                        }
                    }
                }
            }
            return true;
        }
    }
}
