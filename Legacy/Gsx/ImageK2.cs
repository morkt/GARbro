//! \file       ImageK2.cs
//! \date       2018 Feb 09
//! \brief      Toyo GSX image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Gsx
{
    [Export(typeof(ImageFormat))]
    public class K2Format : ImageFormat
    {
        public override string         Tag { get { return "K2"; } }
        public override string Description { get { return "Toyo GSX image format"; } }
        public override uint     Signature { get { return 0x18324B; } } // 'K2'

        public K2Format ()
        {
            Signatures = new uint[] { 0x18324B, 0x20324B, 0x10324B, 0x0F324B, 0x08324B, 0x04324B, 0x01324B };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var bmp_header = Decompress (file, 0x36);
            using (var bmp = new BinMemoryStream (bmp_header))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var bmp_data = Decompress (file);
            using (var bmp = new BinMemoryStream (bmp_data))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("K2Format.Write not implemented");
        }

        internal byte[] Decompress (IBinaryStream input)
        {
            input.Position = 6;
            int unpacked_size = input.ReadInt32();
            return Decompress (input, unpacked_size);
        }

        internal byte[] Decompress (IBinaryStream input, int unpacked_size)
        {
            input.Position = 0x12;
            int data_pos = input.ReadInt32();
            int bits_length = Math.Min (data_pos-0x10, (unpacked_size + 7) / 8);
            var ctl_bits = input.ReadBytes (bits_length);
            input.Position = 6 + data_pos;
            var output = new byte[unpacked_size];

            using (var mem = new MemoryStream (ctl_bits))
            using (var bits = new MsbBitStream (mem))
            using (var data = new MsbBitStream (input.AsStream, true))
            {
                int dst = 0;
                while (dst < unpacked_size)
                {
                    int ctl = bits.GetNextBit();
                    if (-1 == ctl)
                        break;
                    if (ctl != 0)
                    {
                        output[dst++] = (byte)data.GetBits (8);
                    }
                    else
                    {
                        int offset, count;
                        if (bits.GetNextBit() != 0)
                        {
                            offset = data.GetBits (14);
                            count = data.GetBits (4) + 3;
                        }
                        else
                        {
                            offset = data.GetBits (9);
                            count = data.GetBits (3) + 2;
                        }
                        count = Math.Min (count, output.Length-dst);
                        Binary.CopyOverlapped (output, dst-offset-1, dst, count);
                        dst += count;
                    }
                }
                return output;
            }
        }
    }
}
