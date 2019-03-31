//! \file       ImageGXT.cs
//! \date       2019 Feb 25
//! \brief      CRI Middleware image format.
//
// Copyright (C) 2019 by morkt
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
using GameRes.Formats.DirectDraw;

namespace GameRes.Formats.Cri
{
    internal class GxtMetaData : ImageMetaData
    {
        public uint TextureOffset;
        public int  TextureLength;
        public int  PaletteIndex;
        public uint Flags;
        public GxtTextureType   TextureType;
        public GxtTextureFormat TextureFormat;
    }

    internal enum GxtTextureType : uint
    {
        Swizzled    = 0x00000000,
        Cube        = 0x40000000,
        Linear      = 0x60000000,
        Tiled       = 0x80000000,
        LinearStrided = 0xC0000000,
    };

    internal enum GxtTextureFormat : uint
    {
        UBC3 = 0x87000000,
    }

    [Export(typeof(ImageFormat))]
    public class GxtFormat : ImageFormat
    {
        public override string         Tag { get { return "GXT"; } }
        public override string Description { get { return "CRI Middleware image format"; } }
        public override uint     Signature { get { return 0x545847; } } // 'GXT'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            if (header.ToInt32 (4) != 0x10000003)
                return null;
            return new GxtMetaData {
                Width  = header.ToUInt16 (0x38),
                Height = header.ToUInt16 (0x3A),
                TextureOffset = header.ToUInt32 (0x20),
                TextureLength = header.ToInt32 (0x24),
                PaletteIndex = header.ToInt32 (0x28),
                Flags = header.ToUInt32 (0x2C),
                TextureType = (GxtTextureType)header.ToUInt32 (0x30),
                TextureFormat = (GxtTextureFormat)header.ToUInt32 (0x34),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GxtMetaData)info;
            file.Position = meta.TextureOffset;
            var data = file.ReadBytes (meta.TextureLength);
            if (GxtTextureFormat.UBC3 == meta.TextureFormat)
            {
                var pixels = UnpackDXT5 (data, meta);
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
            else
                throw new NotSupportedException (string.Format ("GXT texture format {0:X8} not supported.", meta.TextureFormat));
        }

        byte[] UnpackDXT5 (byte[] input, GxtMetaData info)
        {
            var decoder = new DxtDecoder (input, info);
            int src = 0;
            for (int y = 0; y < info.iHeight; y += 4)
            for (int x = 0; x < info.iWidth; x += 4)
            {
                int px, py;
                GetSwizzledCoords (x / 4, y / 4, info.iWidth / 4, info.iHeight / 4, out px, out py);
                decoder.DecompressDXT5Block (input, src, py * 4, px * 4);
                src += 16;
            }
            return decoder.Output;
        }

        void GetSwizzledCoords (int origX, int origY, int width, int height, out int trX, out int trY)
        {
            if (width == 0)
                width = 16;
            if (height == 0)
                height = 16;

            int i = (origY * width) + origX;

            int min = Math.Min (width, height);
            int k = BitScanReverse ((uint)min); // Math.Log (min, 2);

            if (height < width)
            {
                // XXXyxyxyx → XXXxxxyyy
                int j = i >> (2 * k) << (2 * k)
                    | (DecodeCoord2Y (i) & (min - 1)) << k
                    | (DecodeCoord2X (i) & (min - 1));
                trX = j / height;
                trY = j % height;
            }
            else
            {
                // YYYyxyxyx → YYYyyyxxx
                int j = i >> (2 * k) << (2 * k)
                    | (DecodeCoord2X (i) & (min - 1)) << k
                    | (DecodeCoord2Y (i) & (min - 1));
                trX = j % width;
                trY = j / width;
            }
        }

        internal static int BitScanReverse (uint x)
        {
            int n = 0;
            while ((x >>= 1) != 0)
                ++n;
            return n;
        }

        private static int DecodeCoord2X (int code)
        {
            return Compact1By1 (code);
        }

        private static int DecodeCoord2Y (int code)
        {
            return Compact1By1 (code >> 1);
        }

        private static int Compact1By1 (int x)
        {
            x &= 0x55555555;
            x = (x ^ (x >> 1)) & 0x33333333;
            x = (x ^ (x >> 2)) & 0x0f0f0f0f;
            x = (x ^ (x >> 4)) & 0x00ff00ff;
            x = (x ^ (x >> 8)) & 0x0000ffff;
            return x;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GxtFormat.Write not implemented");
        }
    }
}
