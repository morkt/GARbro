//! \file       ImageXMG.cs
//! \date       2022 Jun 19
//! \brief      ZyX image format.
//
// Copyright (C) 2022 by morkt
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Ikura
{
    [Export(typeof(ImageFormat))]
    public class XmgFormat : ImageFormat
    {
        public override string         Tag { get { return "XMG"; } }
        public override string Description { get { return "ZyX image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".xmg"))
                return null;
            var header = file.ReadBytes (12);
            Decrypt (header);
            if (header[2] != 0 || header[3] != 0)
                return null;
            int width  = LittleEndian.ToInt16 (header, 4);
            int height = LittleEndian.ToInt16 (header, 6);
            if (width <= 0 || height <= 0)
                return null;
            return new ImageMetaData {
                Width  = (uint)width,
                Height = (uint)height,
                BPP    = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 12;
            var palette_data = file.ReadBytes (0x300);
            Decrypt (palette_data, (byte)(12 * 7));
            var palette = ConvertPalette (palette_data);
            var pixels = new byte[info.iWidth * info.iHeight];
            int dst = 0;
            for (int y = 0; y < info.iHeight; ++y)
            {
                int x = 0;
                while (x < info.iWidth)
                {
                    byte ctl = file.ReadUInt8();
                    int offset;
                    int count;
                    if ((ctl & 0xC0) != 0)
                    {
                        if ((ctl & 0x80) != 0)
                        {
                            offset = -((ctl << 8 | file.ReadUInt8()) & 0xFFF) - 1;
                            count = (ctl & 0x70) >> 4;
                            if (count != 0)
                                count += 2;
                            else
                                count = file.ReadUInt8() + 10;
                            Binary.CopyOverlapped (pixels, dst + offset, dst, count);
                        }
                        else
                        {
                            count = ctl & 0x3F;
                            if (0 == count)
                                count = 64 + file.ReadUInt8();
                            count += 1;
                            byte p = pixels[dst-1];
                            for (int i = 0; i < count; ++i)
                                pixels[dst+i] = p;
                        }
                    }
                    else
                    {
                        count = ctl & 0x3F;
                        if (0 == count)
                            count = 64 + file.ReadUInt8();
                        file.Read (pixels, dst, count);
                    }
                    x += count;
                    dst += count;
                }
            }
            return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("xxxFormat.Write not implemented");
        }

        internal static BitmapPalette ConvertPalette (byte[] palette_data)
        {
            const int colors = 0x100;
            var color_map = new Color[colors];
            int src = 0;
            for (int i = 0; i < colors; ++i)
            {
                color_map[i] = Color.FromRgb (palette_data[src+1], palette_data[src+2], palette_data[src]);
                src += 3;
            }
            return new BitmapPalette (color_map);
        }

        internal static byte Decrypt (byte[] data, byte key = 0)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                byte v = (byte)((data[i] - key) ^ 0xF3);
                data[i] = v;
                key += 7;
            }
            return key;
        }
    }
}
