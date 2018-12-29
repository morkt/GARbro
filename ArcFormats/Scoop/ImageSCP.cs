//! \file       ImageSCP.cs
//! \date       2018 Dec 27
//! \brief      Scoop compressed bitmap.
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

namespace GameRes.Formats.Scoop
{
    [Export(typeof(ImageFormat))]
    public class ScpFormat : ImageFormat
    {
        public override string         Tag { get { return "SCP"; } }
        public override string Description { get { return "Scoop compressed bitmap"; } }
        public override uint     Signature { get { return 0x7A504353; } } // 'SCPz'

        public ScpFormat ()
        {
            Extensions = new string[] { };
        }

        const uint DefaultHeaderKey = 0x65641538;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            uint length = file.ReadUInt32() ^ DefaultHeaderKey;
            if (length <= 0)
                return null;
            var header = new byte[0x36];
            Unpack (file, header);
            if (!header.AsciiEqual ("BM"))
                return null;
            using (var bmp = new BinMemoryStream (header))
            {
                var info = Bmp.ReadMetaData (bmp) as BmpMetaData;
                if (info != null)
                    info.ImageLength = length;
                return info;
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (BmpMetaData)info;
            var data = new byte[meta.ImageLength];
            file.Position = 8;
            Unpack (file, data);
            using (var bmp = new BinMemoryStream (data))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ScpFormat.Write not implemented");
        }

        internal void Unpack (IBinaryStream input, byte[] output)
        {
            uint bits = 0;
            int dst = 0;
            byte v = 0x7F;
            byte x = 0;
            while (dst < output.Length)
            {
                uint ctl = bits >> 31;
                bits <<= 1;
                if (0 == bits)
                {
                    bits = input.ReadUInt32();
                    continue;
                }
                if (0 == ctl)
                {
                    x = v;
                    v ^= input.ReadUInt8();
                    output[dst++] = v;
                }
                else
                {
                    ushort z = input.ReadUInt16();
                    z += (ushort)bits;
                    int count = (z >> 12) & 0xF;
                    if (0 == count)
                    {
                        count = (x + input.ReadUInt8()) & 0xFF;
                        if (0 == count)
                            break;
                        count += 15;
                    }
                    int offset = ~(z & 0xFFF);
                    count = Math.Min (count + 2, output.Length - dst);
                    Binary.CopyOverlapped (output, dst + offset, dst, count);
                    dst += count;
                }
            }
        }
    }
}
