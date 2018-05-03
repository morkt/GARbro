//! \file       ImageAF.cs
//! \date       2018 May 03
//! \brief      TanukiSoft bitmap.
//
// Copyright (C) 2018 by morkt
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
using GameRes.Compression;

namespace GameRes.Formats.Tanuki
{
    [Export(typeof(ImageFormat))]
    public class AmapFormat : ImageFormat
    {
        public override string         Tag { get { return "AMAP"; } }
        public override string Description { get { return "TanukiSoft bitmap format"; } }
        public override uint     Signature { get { return 0x50414D41; } } // 'AMAP'

        public AmapFormat ()
        {
            Extensions = new[] { "af" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            return new ImageMetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            var pixels = LzssUnpack (file);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AmapFormat.Write not implemented");
        }

        byte[] LzssUnpack (IBinaryStream input)
        {
            int unpacked_size = input.ReadInt32();
            var output = new byte[unpacked_size];
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            int dst = 0;
            int ctl = 0;
            while (dst < unpacked_size)
            {
                ctl >>= 1;
                if (0 == (ctl & 0x100))
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        break;
                    ctl |= 0xFF00;
                }
                if (0 != (ctl & 1))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    output[dst++] = frame[frame_pos++ & 0xFFF] = (byte)b;
                }
                else
                {
                    int lo = input.ReadByte();
                    if (-1 == lo)
                        break;
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        break;
                    int offset = (hi & 0xF0) << 4 | lo;
                    for (int count = 3 + (~hi & 0xF); count != 0; --count)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        output[dst++] = frame[frame_pos++ & 0xFFF] = v;
                    }
                }
            }
            return output;
        }
    }
}
