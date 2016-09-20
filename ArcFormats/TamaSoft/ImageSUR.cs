//! \file       ImageSUR.cs
//! \date       Tue Sep 20 10:56:45 2016
//! \brief      TamaSoft image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Tama
{
    [Export(typeof(ImageFormat))]
    public class SurFormat : ImageFormat
    {
        public override string         Tag { get { return "SUR"; } }
        public override string Description { get { return "TamaSoft ADV system image"; } }
        public override uint     Signature { get { return 0x52555345; } } // 'ESUR'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x10];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new ImageMetaData
            {
                Width  = LittleEndian.ToUInt32 (header, 8),
                Height = LittleEndian.ToUInt32 (header, 12),
                BPP    = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var pixels = new byte[info.Width * info.Height * 4];
            stream.Position = 0x20;
            UnpackLzss (stream, pixels);
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("SurFormat.Write not implemented");
        }

        /// <summary>
        /// Differs from a common LZSS implementation by frame offset encoding.
        /// </summary>
        void UnpackLzss (Stream input, byte[] output)
        {
            int dst = 0;
            var frame = new byte[0x1000];
            int frame_pos = 0xFEE;
            const int frame_mask = 0xFFF;
            int ctl = 2;
            while (dst < output.Length)
            {
                ctl >>= 1;
                if (1 == ctl)
                {
                    ctl = input.ReadByte();
                    if (-1 == ctl)
                        throw new EndOfStreamException();
                    ctl |= 0x100;
                }
                if (0 != (ctl & 1))
                {
                    byte b = (byte)input.ReadByte();
                    frame[frame_pos++] = b;
                    frame_pos &= frame_mask;
                    output[dst++] = b;
                }
                else
                {
                    int lo = input.ReadByte();
                    int hi = input.ReadByte();
                    if (-1 == hi)
                        throw new EndOfStreamException();
                    int offset = hi >> 4 | lo << 4;
                    int count = Math.Min (3 + (hi & 0xF), output.Length - dst);
                    while (count --> 0)
                    {
                        byte v = frame[offset++];
                        offset &= frame_mask;
                        frame[frame_pos++] = v;
                        frame_pos &= frame_mask;
                        output[dst++] = v;
                    }
                }
            }
        }
    }
}
