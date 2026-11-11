//! \file       ImageTIM.cs
//! \date       2026 Jan 05
//! \brief      Standard PlayStation (PS1) image format.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Sony
{
    internal class TimMetaData : ImageMetaData
    {
        public uint BppMode;
        public bool HasClut;
        public uint ClutOffset;
        public uint ImageOffset;
    }

    [Export(typeof(ImageFormat))]
    public class TimFormat : ImageFormat
    {
        public override string Tag { get { return "TIM"; } }
        public override string Description { get { return "PlayStation image format"; } }
        public override uint Signature { get { return 0x00000010; } } // Magic: 10 00 00 00

        public override ImageMetaData ReadMetaData(IBinaryStream stream)
        {
            var header = stream.ReadHeader(8);
            if (header[0] != 0x10 || header[1] != 0)
                return null;

            ushort flag = header.ToUInt16(4);
            uint mode = (uint)(flag & 3);
            bool hasClut = (flag & 8) != 0;

            uint currentOffset = 8;
            uint clutOffset = 0;

            if (hasClut)
            {
                clutOffset = currentOffset;
                stream.Position = currentOffset;
                uint clutSize = stream.ReadUInt32();
                currentOffset += clutSize;
            }

            uint imageOffset = currentOffset;
            if (imageOffset + 12 > stream.Length) return null;

            // Image Header: BlockSize(4), VramX(2), VramY(2), WordWidth(2), Height(2)
            stream.Position = imageOffset + 8;
            ushort wordWidth = stream.ReadUInt16();
            ushort height = stream.ReadUInt16();

            int bpp;
            int width;
            switch (mode)
            {
                case 0: bpp = 4; width = wordWidth * 4; break;
                case 1: bpp = 8; width = wordWidth * 2; break;
                case 2: bpp = 16; width = wordWidth; break;
                case 3: bpp = 24; width = (wordWidth * 2) / 3; break;
                default: return null;
            }

            return new TimMetaData
            {
                Width = (uint)width,
                Height = height,
                BPP = bpp,
                BppMode = mode,
                HasClut = hasClut,
                ClutOffset = clutOffset,
                ImageOffset = imageOffset
            };
        }

        public override ImageData Read(IBinaryStream stream, ImageMetaData info)
        {
            var meta = (TimMetaData)info;
            int width = (int)meta.Width;
            int height = (int)meta.Height;
            int row_size = GetRowSize (meta.BppMode, width);

            // CLUT is decoded into raw 16-bit words; missing entries read as black.
            var colors = new ushort[256];
            if (meta.HasClut)
            {
                // CLUT Header: BlockSize(4), X(2), Y(2), Width(2), Height(2)
                // Width/Height are at offsets 8 and 10 within the CLUT block.
                stream.Position = meta.ClutOffset + 8;
                long total_colors = (long)stream.ReadUInt16 () * stream.ReadUInt16 ();
                if (total_colors < 1 || total_colors > 256)
                    throw new InvalidFormatException ();
                for (int i = 0; i < total_colors; ++i)
                    colors[i] = stream.ReadUInt16 ();
            }
            // Image data starts at ImageOffset + 12 (skipping the 12-byte header)
            stream.Position = meta.ImageOffset + 12;
            var pixels = new byte[width * height * 4];
            int dst = 0;
            for (int y = 0; y < height; ++y)
            {
                var row = stream.ReadBytes (row_size);
                if (row.Length != row_size)
                    throw new InvalidFormatException ();
                switch (meta.BppMode)
                {
                    case 0: // 4bpp, left pixel in the low nibble
                        for (int x = 0; x < width; ++x)
                        {
                            byte b = row[x >> 1];
                            int index = 0 == (x & 1) ? b & 0x0F : (b >> 4) & 0x0F;
                            WritePixel (pixels, ref dst, colors[index]);
                        }
                        break;
                    case 1: // 8bpp
                        for (int x = 0; x < width; ++x)
                            WritePixel (pixels, ref dst, colors[row[x]]);
                        break;
                    case 2: // 16bpp
                        for (int x = 0; x < width; ++x)
                        {
                            ushort c = (ushort)(row[x*2] | row[x*2+1] << 8);
                            WritePixel (pixels, ref dst, c);
                        }
                        break;
                    default: // 24bpp, stored as RGB triplets
                        for (int x = 0; x < width; ++x, dst += 4)
                        {
                            int src = x * 3;
                            pixels[dst+0] = row[src+2];
                            pixels[dst+1] = row[src+1];
                            pixels[dst+2] = row[src+0];
                            pixels[dst+3] = 255;
                        }
                        break;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        // PS1 16-bit color: bit 15=STP, 14-10=B, 9-5=G, 4-0=R;
        // black (0,0,0) is used as a transparent colour key.
        static void WritePixel (byte[] dst, ref int pos, ushort c)
        {
            dst[pos+0] = Expand5to8 ((c >> 10) & 0x1F);
            dst[pos+1] = Expand5to8 ((c >> 5) & 0x1F);
            dst[pos+2] = Expand5to8 (c & 0x1F);
            dst[pos+3] = 0 == c ? (byte)0 : (byte)255;
            pos += 4;
        }

        static byte Expand5to8 (int v)
        {
            return (byte)((v << 3) | (v >> 2));
        }

        // Rows are padded to 16-bit word boundaries (stored pixel word width).
        static int GetRowSize (uint mode, int width)
        {
            switch (mode)
            {
                case 0: return ((width + 3) / 4) * 2;
                case 1: return ((width + 1) / 2) * 2;
                case 2: return width * 2;
                default: return ((width * 3 + 1) / 2) * 2;
            }
        }

        public override void Write(Stream file, ImageData image)
        {
            throw new NotImplementedException();
        }
    }
}
