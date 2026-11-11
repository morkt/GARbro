//! \file       Ps2ImageUtil.cs
//! \date       2026-09-30
//! \brief      PS2 texture palette and pixel conversions shared by Artdink formats.
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
// Semantics follow the reference implementation of the Artdink formats
// (Verviewer Utils.ImageUtils).

using System;

namespace GameRes.Formats.Artdink
{
    internal static class Ps2ImageUtil
    {
        /// <summary>
        /// PS2 alpha mapping: a*2-1 clamped to [0,255] (0x80 is fully opaque).
        /// </summary>
        public static byte FixAlpha (byte a)
        {
            int v = a * 2 - 1;
            if (v < 0)
                v = 0;
            if (v > 255)
                v = 255;
            return (byte)v;
        }

        /// <summary>
        /// Convert an RGBA palette to BGRA, optionally applying the PS2 alpha mapping.
        /// </summary>
        public static byte[] BuildPalette (byte[] rgba, int color_count, bool ps2_alpha)
        {
            var bgra = new byte[color_count * 4];
            for (int i = 0; i < color_count; ++i)
            {
                int p = i * 4;
                bgra[p]   = rgba[p+2];
                bgra[p+1] = rgba[p+1];
                bgra[p+2] = rgba[p];
                bgra[p+3] = ps2_alpha ? FixAlpha (rgba[p+3]) : rgba[p+3];
            }
            return bgra;
        }

        /// <summary>
        /// Reorder an RGBA palette stored in PS2 32-color blocks: within each block
        /// the colors are laid out as 0-7, 16-23, 8-15, 24-31.
        /// </summary>
        public static byte[] ReorderBlock32 (byte[] rgba, int color_count)
        {
            var reordered = new byte[color_count * 4];
            int dst = 0;
            for (int block = 0; block < color_count; block += 32)
            {
                CopyColors (rgba, block,    8, reordered, ref dst);
                CopyColors (rgba, block+16, 8, reordered, ref dst);
                CopyColors (rgba, block+8,  8, reordered, ref dst);
                CopyColors (rgba, block+24, 8, reordered, ref dst);
            }
            return reordered;
        }

        static void CopyColors (byte[] src, int first, int count, byte[] dst, ref int dst_index)
        {
            Buffer.BlockCopy (src, first * 4, dst, dst_index * 4, count * 4);
            dst_index += count;
        }

        /// <summary>
        /// Build a 256-color BGRA palette from a PS2 block-ordered RGBA CLUT.
        /// </summary>
        public static byte[] BuildPs2Palette256 (byte[] rgba)
        {
            return BuildPalette (ReorderBlock32 (rgba, 256), 256, true);
        }

        /// <summary>Convert a row of packed 4bpp indices (low nibble first) to BGRA.</summary>
        public static void ConvertRow4 (byte[] src, int src_offset, byte[] dst, int dst_offset, int width, byte[] palette)
        {
            int d = dst_offset;
            for (int x = 0; x < width; ++x)
            {
                int b = src[src_offset + (x >> 1)];
                int index = (x & 1) == 0 ? (b & 0x0F) : ((b >> 4) & 0x0F);
                Buffer.BlockCopy (palette, index * 4, dst, d, 4);
                d += 4;
            }
        }

        /// <summary>Convert a row of 8bpp indices to BGRA.</summary>
        public static void ConvertRow8 (byte[] src, int src_offset, byte[] dst, int dst_offset, int width, byte[] palette)
        {
            int d = dst_offset;
            for (int x = 0; x < width; ++x)
            {
                Buffer.BlockCopy (palette, src[src_offset + x] * 4, dst, d, 4);
                d += 4;
            }
        }

        /// <summary>Convert a row of PS2 16bpp RGB555 (R in the low bits) to BGRA.</summary>
        public static void ConvertRow555 (byte[] src, int src_offset, byte[] dst, int dst_offset, int width)
        {
            int d = dst_offset;
            for (int x = 0; x < width; ++x)
            {
                int v = src[src_offset] | (src[src_offset+1] << 8);
                src_offset += 2;
                int r5 = v & 0x1F;
                int g5 = (v >> 5) & 0x1F;
                int b5 = (v >> 10) & 0x1F;
                dst[d]   = (byte)((b5 << 3) | (b5 >> 2));
                dst[d+1] = (byte)((g5 << 3) | (g5 >> 2));
                dst[d+2] = (byte)((r5 << 3) | (r5 >> 2));
                dst[d+3] = 0xFF;
                d += 4;
            }
        }

        /// <summary>Convert a row of 24bpp RGB to BGRA.</summary>
        public static void ConvertRow24 (byte[] src, int src_offset, byte[] dst, int dst_offset, int width)
        {
            int d = dst_offset;
            for (int x = 0; x < width; ++x)
            {
                dst[d]   = src[src_offset+2];
                dst[d+1] = src[src_offset+1];
                dst[d+2] = src[src_offset];
                dst[d+3] = 0xFF;
                src_offset += 3;
                d += 4;
            }
        }

        /// <summary>Convert a row of 32bpp RGBA to BGRA applying the PS2 alpha mapping.</summary>
        public static void ConvertRow32 (byte[] src, int src_offset, byte[] dst, int dst_offset, int width)
        {
            int d = dst_offset;
            for (int x = 0; x < width; ++x)
            {
                dst[d]   = src[src_offset+2];
                dst[d+1] = src[src_offset+1];
                dst[d+2] = src[src_offset];
                dst[d+3] = FixAlpha (src[src_offset+3]);
                src_offset += 4;
                d += 4;
            }
        }
    }
}