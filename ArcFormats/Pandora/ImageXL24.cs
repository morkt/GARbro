//! \file       ImageXL24.cs
//! \date       Thu Jan 05 02:18:34 2017
//! \brief      Pandora.box image format.
//
// Copyright (C) 2017 by morkt
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Terios
{
    [Export(typeof(ImageFormat))]
    public class Xl24Format : ImageFormat
    {
        public override string         Tag { get { return "XL24"; } }
        public override string Description { get { return "\"Pandora.box\" image format"; } }
        public override uint     Signature { get { return 0x34324C58; } } // 'XL24'

        public Xl24Format ()
        {
            Extensions = new string[] { "bmp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 8;
            uint width  = file.ReadUInt32();
            uint height = file.ReadUInt32();
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            int stride = (int)info.Width * 3;
            var pixels = new byte[info.Height * stride];
            int prev_line = 0;
            int cur_line = pixels.Length - stride;
            while (cur_line >= 0)
            {
                int dst = cur_line;
                for (int x = (int)info.Width; x > 0; )
                {
                    byte ctl = file.ReadUInt8();
                    int count;
                    switch (ctl)
                    {
                    case 0:
                        count = file.ReadUInt8() + 2;
                        dst += 3 * count;
                        x -= count;
                        break;
                    case 1:
                        dst += 3;
                        --x;
                        break;
                    case 0x80:
                        x = 0;
                        break;
                    case 0xFF:
                        file.Read (pixels, dst, 3);
                        if (--x > 0)
                            Binary.CopyOverlapped (pixels, dst, dst+3, x * 3);
                        x = 0;
                        break;
                    default:
                        if (ctl > 0x80)
                        {
                            count = ctl - 0x80;
                            file.Read (pixels, dst, 3);
                            if (count > 1)
                                Binary.CopyOverlapped (pixels, dst, dst+3, (count - 1) * 3);
                            dst += count * 3;
                            x -= count;
                        }
                        else
                        {
                            count = 3 * ctl;
                            file.Read (pixels, dst, count);
                            dst += count;
                            x -= ctl;
                        }
                        break;
                    }
                }
                if (prev_line != 0)
                {
                    for (int i = 0; i < stride; ++i)
                        pixels[cur_line+i] ^= pixels[prev_line+i];
                }
                prev_line = cur_line;
                cur_line -= stride;
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Xl24Format.Write not implemented");
        }
    }
}
