//! \file       ImageT32.cs
//! \date       2026-09-30
//! \brief      Ikusabune T32 image format.
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
using GameRes.Utility;

namespace GameRes.Formats.Broccoli
{
    internal class T32MetaData : ImageMetaData
    {
        public int  Parts;
        public int  OffsetBase;
        public bool IsT8;
        public bool Is1555;
        public bool IsBE;
    }

    [Export(typeof(ImageFormat))]
    public class T32Format : ImageFormat
    {
        public override string         Tag { get { return "T32"; } }
        public override string Description { get { return "Ikusabune T32 image format"; } }
        public override uint     Signature { get { return 0x20323354; } } // 'T32 '

        public T32Format ()
        {
            Extensions = new string[] { "t32" };
            Signatures = new uint[] { 0x20323354, 0x34343434, 0x35353531,   // 'T32 ' '4444' '1555'
                                      0x42613854, 0x42613454, 0x42613154,   // 'T8aB' 'T4aB' 'T1aB'
                                      0x44613854, 0x43613854,               // 'T8aD' 'T8aC'
                                      0x44613454, 0x43613454,               // 'T4aD' 'T4aC'
                                      0x44613154, 0x43613154 };             // 'T1aD' 'T1aC'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20).ToArray ();
            uint magic = LittleEndian.ToUInt32 (header, 0);
            bool is_t8, is_1555, old, be;
            switch (magic)
            {
            case 0x20323354: is_t8 = true;  is_1555 = false; old = true;  be = false; break;
            case 0x34343434: is_t8 = false; is_1555 = false; old = true;  be = false; break;
            case 0x35353531: is_t8 = false; is_1555 = true;  old = true;  be = false; break;
            case 0x42613854: is_t8 = true;  is_1555 = false; old = false; be = false; break;
            case 0x42613454: is_t8 = false; is_1555 = false; old = false; be = false; break;
            case 0x42613154: is_t8 = false; is_1555 = true;  old = false; be = false; break;
            case 0x44613854: case 0x43613854:
                is_t8 = true;  is_1555 = false; old = false; be = true;  break;
            case 0x44613454: case 0x43613454:
                is_t8 = false; is_1555 = false; old = false; be = true;  break;
            case 0x44613154: case 0x43613154:
                is_t8 = false; is_1555 = true;  old = false; be = true;  break;
            default: return null;
            }
            uint width  = GetUInt32 (header, 20, be);
            uint height = GetUInt32 (header, 24, be);
            int  parts  = (int)GetUInt32 (header, 28, be);
            if (0 == width || 0 == height || width > 0x4000 || height > 0x4000 || parts < 0)
                return null;
            return new T32MetaData {
                Width = width, Height = height, BPP = is_t8 ? 32 : 16,
                Parts = parts, OffsetBase = be ? 44 : (old ? 32 : 36),
                IsT8 = is_t8, Is1555 = is_1555, IsBE = be,
            };
        }

        internal static uint GetUInt32 (byte[] header, int index, bool be)
        {
            return be ? Binary.BigEndian (LittleEndian.ToUInt32 (header, index))
                      : LittleEndian.ToUInt32 (header, index);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (T32MetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            int bytes_per_pixel = meta.IsT8 ? 4 : 2;
            var pixels = new byte[width*height*4];
            if (meta.IsBE && 0 == meta.Parts)
            {
                // console variant with no parts table: raw pixels start at OffsetBase
                int pitch = (width*bytes_per_pixel + 3) & ~3;
                if (meta.OffsetBase + (long)pitch*height > file.Length)
                    throw new InvalidFormatException ();
                file.Position = meta.OffsetBase;
                var row = new byte[pitch];
                for (int y = 0; y < height; ++y)
                {
                    file.Read (row, 0, pitch);
                    DecodeBeRow (row, pixels, y*width*4, width, meta);
                }
                return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
            }
            file.Position = meta.OffsetBase;
            var table = file.ReadBytes (4*meta.Parts);
            var offsets = new uint[meta.Parts];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = GetUInt32 (table, i*4, meta.IsBE);
            foreach (var ofs in offsets)
            {
                if ((long)ofs + 16 > file.Length)
                    continue;
                file.Position = ofs;
                var rect = file.ReadBytes (16);
                int px = (int)GetUInt32 (rect, 0, meta.IsBE);
                int py = (int)GetUInt32 (rect, 4, meta.IsBE);
                int pw = (int)GetUInt32 (rect, 8, meta.IsBE);
                int ph = (int)GetUInt32 (rect, 12, meta.IsBE);
                if (pw <= 0 || ph <= 0)
                    continue;
                if (px < 0 || py < 0 || px >= width || py >= height)
                    continue;
                int cw = Math.Min (pw, width - px);
                int ch = Math.Min (ph, height - py);
                if (cw <= 0 || ch <= 0)
                    continue;
                int  pitch = (pw*bytes_per_pixel + 3) & ~3;
                long start = (long)ofs + 16;
                if (start + (long)pitch*ph > file.Length)
                    continue;
                file.Position = start;
                var row = new byte[pitch];
                for (int y = 0; y < ch; ++y)
                {
                    file.Read (row, 0, pitch);
                    int dst = ((py+y)*width + px) * 4;
                    if (meta.IsBE)
                        DecodeBeRow (row, pixels, dst, cw, meta);
                    else if (meta.IsT8)
                        Buffer.BlockCopy (row, 0, pixels, dst, cw*4);
                    else if (meta.Is1555)
                        DecodeRow1555 (row, pixels, dst, cw);
                    else
                        DecodeRow4444 (row, pixels, dst, cw);
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        // big-endian rows -> BGRA: 32bpp bytes are A,R,G,B; 16bpp words are big-endian
        static void DecodeBeRow (byte[] src, byte[] pixels, int dst, int count, T32MetaData meta)
        {
            if (meta.IsT8)
            {
                for (int x = 0; x < count; ++x)
                {
                    int s = x*4;
                    int d = dst + x*4;
                    pixels[d  ] = src[s+3]; // B
                    pixels[d+1] = src[s+2]; // G
                    pixels[d+2] = src[s+1]; // R
                    pixels[d+3] = src[s  ]; // A
                }
            }
            else
            {
                for (int x = 0; x < count; ++x)
                {
                    int v = (src[x*2] << 8) | src[x*2+1];
                    int d = dst + x*4;
                    if (meta.Is1555)
                    {
                        int r = (v >> 10) & 0x1F;
                        int g = (v >>  5) & 0x1F;
                        int b =  v        & 0x1F;
                        pixels[d  ] = (byte)((b << 3) | (b >> 2));
                        pixels[d+1] = (byte)((g << 3) | (g >> 2));
                        pixels[d+2] = (byte)((r << 3) | (r >> 2));
                        pixels[d+3] = (byte)(0 != ((v >> 15) & 1) ? 0xFF : 0x00);
                    }
                    else
                    {
                        int a = (v >> 12) & 0xF;
                        int r = (v >>  8) & 0xF;
                        int g = (v >>  4) & 0xF;
                        int b =  v        & 0xF;
                        pixels[d  ] = (byte)((b << 4) | b);
                        pixels[d+1] = (byte)((g << 4) | g);
                        pixels[d+2] = (byte)((r << 4) | r);
                        pixels[d+3] = (byte)((a << 4) | a);
                    }
                }
            }
        }

        static void DecodeRow1555 (byte[] src, byte[] pixels, int dst, int count)
        {
            for (int x = 0; x < count; ++x)
            {
                int v = src[x*2] | (src[x*2+1] << 8);
                int r = (v >> 10) & 0x1F;
                int g = (v >>  5) & 0x1F;
                int b =  v        & 0x1F;
                pixels[dst+x*4  ] = (byte)((b << 3) | (b >> 2));
                pixels[dst+x*4+1] = (byte)((g << 3) | (g >> 2));
                pixels[dst+x*4+2] = (byte)((r << 3) | (r >> 2));
                pixels[dst+x*4+3] = (byte)(0 != ((v >> 15) & 1) ? 0xFF : 0x00);
            }
        }

        static void DecodeRow4444 (byte[] src, byte[] pixels, int dst, int count)
        {
            for (int x = 0; x < count; ++x)
            {
                int v = src[x*2] | (src[x*2+1] << 8);
                int a = (v >> 12) & 0xF;
                int r = (v >>  8) & 0xF;
                int g = (v >>  4) & 0xF;
                int b =  v        & 0xF;
                pixels[dst+x*4  ] = (byte)((b << 4) | b);
                pixels[dst+x*4+1] = (byte)((g << 4) | g);
                pixels[dst+x*4+2] = (byte)((r << 4) | r);
                pixels[dst+x*4+3] = (byte)((a << 4) | a);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("T32Format.Write not implemented");
        }
    }
}
