//! \file       ImagePGD.cs
//! \date       Sat Feb 13 13:56:06 2016
//! \brief      Image format used by subsidiaries of Amuse Craft (former Softpal).
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Softpal
{
    [Export(typeof(ImageFormat))]
    public class Pgd11Format : ImageFormat
    {
        public override string         Tag { get { return "PGD/11_C"; } }
        public override string Description { get { return "Image format used by Softpal subsidiaries"; } }
        public override uint     Signature { get { return 0x1C4547; } } // 'GE\x1C'

        public Pgd11Format ()
        {
            Extensions = new string[] { "pgd" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x20];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 0x1C, "11_C"))
                return null;
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0x0C),
                Height  = LittleEndian.ToUInt32 (header, 0x10),
                OffsetX = LittleEndian.ToInt32 (header, 4),
                OffsetY = LittleEndian.ToInt32 (header, 8),
                BPP     = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            stream.Position = 0x20;
            using (var reader = new ArcView.Reader (stream))
            {
                int unpacked_size = reader.ReadInt32();
                reader.ReadInt32(); // packed_size
                var planes = Unpack (reader, unpacked_size);
                var pixels = new byte[planes.Length];
                int plane_size = (int)info.Width*(int)info.Height;
                int b_src = 0;
                int g_src = b_src+plane_size;
                int r_src = g_src+plane_size;
                int alpha_src = r_src+plane_size;
                int dst = 0;
                while (dst < pixels.Length)
                {
                    pixels[dst++] = planes[b_src++];
                    pixels[dst++] = planes[g_src++];
                    pixels[dst++] = planes[r_src++];
                    pixels[dst++] = planes[alpha_src++];
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
        }

        byte[] Unpack (BinaryReader input, int unpacked_size)
        {
            var output = new byte[unpacked_size];
            int dst = 0;
            int ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    ctl = input.ReadByte() | 0x100;
                }
                int count;
                if (0 != (ctl & 1))
                {
                    int src = input.ReadUInt16();
                    count = input.ReadByte();
                    if (dst >= 0xFFC)
                        src += dst - 0xFFC;
                    Binary.CopyOverlapped (output, src, dst, count);
                }
                else
                {
                    count = input.ReadByte();
                    input.Read (output, dst, count);
                }
                dst += count;
            }
            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Pgd11Format.Write not implemented");
        }
    }
}
