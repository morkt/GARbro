//! \file       ImageTbm.cs
//! \date       2026-09-30
//! \brief      Ikusabune TBM image format.
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
    internal class TbmMetaData : ImageMetaData
    {
        public int  Parts;
        public int  OffsetBase;
        public bool IsBE;
    }

    [Export(typeof(ImageFormat))]
    public class TbmFormat : ImageFormat
    {
        public override string         Tag { get { return "TBM"; } }
        public override string Description { get { return "Ikusabune TBM image format"; } }
        public override uint     Signature { get { return 0x204D4254; } } // 'TBM '

        public TbmFormat ()
        {
            Extensions = new string[] { "tbm" };
            Signatures = new uint[] { 0x204D4254, 0x424D4254,   // 'TBM ' 'TBMB'
                                      0x444D4254, 0x434D4254 }; // 'TBMD' 'TBMC'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x24).ToArray ();
            uint magic = LittleEndian.ToUInt32 (header, 0);
            bool old, be;
            if (0x204D4254 == magic)       // 'TBM '
            {
                old = true;  be = false;
            }
            else if (0x424D4254 == magic)  // 'TBMB'
            {
                old = false; be = false;
            }
            else if (0x444D4254 == magic || 0x434D4254 == magic)   // 'TBMD' 'TBMC'
            {
                old = false; be = true;
            }
            else
                return null;
            int bpp = (int)T32Format.GetUInt32 (header, 32, be);
            if (be)
            {
                if (24 != bpp && 32 != bpp)
                    return null;
            }
            else if (16 != bpp && 24 != bpp)
                return null;
            uint width  = T32Format.GetUInt32 (header, 20, be);
            uint height = T32Format.GetUInt32 (header, 24, be);
            int  parts  = (int)T32Format.GetUInt32 (header, 28, be);
            if (0 == width || 0 == height || width > 0x4000 || height > 0x4000 || parts < 0)
                return null;
            return new TbmMetaData {
                Width = width, Height = height, BPP = bpp,
                Parts = parts, OffsetBase = be ? 52 : (old ? 40 : 44), IsBE = be,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TbmMetaData)info;
            int width  = (int)meta.Width;
            int height = (int)meta.Height;
            int bytes_per_pixel = meta.BPP / 8;
            var pixels = new byte[width*height*4];
            file.Position = meta.OffsetBase;
            var table = file.ReadBytes (4*meta.Parts);
            var offsets = new uint[meta.Parts];
            for (int i = 0; i < offsets.Length; ++i)
                offsets[i] = T32Format.GetUInt32 (table, i*4, meta.IsBE);
            foreach (var ofs in offsets)
            {
                if ((long)ofs + 16 > file.Length)
                    continue;
                file.Position = ofs;
                var rect = file.ReadBytes (16);
                int px = (int)T32Format.GetUInt32 (rect, 0, meta.IsBE);
                int py = (int)T32Format.GetUInt32 (rect, 4, meta.IsBE);
                int pw = (int)T32Format.GetUInt32 (rect, 8, meta.IsBE);
                int ph = (int)T32Format.GetUInt32 (rect, 12, meta.IsBE);
                if (pw <= 0 || ph <= 0)
                    continue;
                long row_size = ((long)pw*bytes_per_pixel + 3) & ~3L;
                if (row_size > int.MaxValue || px <= -pw || py <= -ph)
                    continue;
                int src_row = (int)row_size;
                int source_height = ph;
                int sx = 0, sy = 0;
                if (px < 0 || py < 0)
                {
                    if (!meta.IsBE)     // little-endian variants skip such parts
                        continue;
                    // console variants clip the source rectangle instead
                    if (px < 0) { sx = -px; pw += px; px = 0; }
                    if (py < 0) { sy = -py; ph += py; py = 0; }
                    if (pw <= 0 || ph <= 0)
                        continue;
                }
                if (px >= width || py >= height)
                    continue;
                int cw = Math.Min (pw, width - px);
                int ch = Math.Min (ph, height - py);
                if (cw <= 0 || ch <= 0)
                    continue;
                long start = (long)ofs + 16;
                if (start + (long)src_row*source_height > file.Length)
                    continue;
                file.Position = start + (long)sy*src_row;   // skip rows clipped off the top
                var row = new byte[src_row];
                for (int y = 0; y < ch; ++y)
                {
                    file.Read (row, 0, src_row);
                    int dst = ((py+y)*width + px) * 4;
                    if (4 == bytes_per_pixel)
                    {
                        // B,G,R,A bytes regardless of file endianness
                        Buffer.BlockCopy (row, sx*4, pixels, dst, cw*4);
                    }
                    else if (3 == bytes_per_pixel)
                    {
                        for (int x = 0; x < cw; ++x)
                        {
                            int s = (sx+x)*3;
                            int d = dst + x*4;
                            pixels[d  ] = row[s  ]; // B
                            pixels[d+1] = row[s+1]; // G
                            pixels[d+2] = row[s+2]; // R
                            pixels[d+3] = 0xFF;
                        }
                    }
                    else // 16bpp: X RRRRR GGGGG BBBBB, no alpha
                    {
                        for (int x = 0; x < cw; ++x)
                        {
                            int s = (sx+x)*2;
                            int v = row[s] | (row[s+1] << 8);
                            int r = (v >> 11) & 0x1F;
                            int g = (v >>  6) & 0x1F;
                            int b =  v        & 0x1F;
                            int d = dst + x*4;
                            pixels[d  ] = (byte)((b << 3) | (b >> 2));
                            pixels[d+1] = (byte)((g << 3) | (g >> 2));
                            pixels[d+2] = (byte)((r << 3) | (r >> 2));
                            pixels[d+3] = 0xFF;
                        }
                    }
                }
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("TbmFormat.Write not implemented");
        }
    }
}
