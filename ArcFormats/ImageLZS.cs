//! \file       ImageLZS.cs
//! \date       2017 Dec 07
//! \brief      LZSS-compressed bitmap.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Misc
{
    internal class LzsMetaData : ImageMetaData
    {
        public bool IsCompressed;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class LzsFormat : ImageFormat
    {
        public override string         Tag { get { return "LZS"; } }
        public override string Description { get { return "LZSS-compressed bitmap"; } }
        public override uint     Signature { get { return 0x53535A4C; } } // 'LZSS'
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            bool is_compressed = (header[12] & 7) != 0;
            using (var input = OpenLzss (file, 0x42, is_compressed))
            {
                var info = Bmp.ReadMetaData (input);
                if (null == info)
                    return null;
                return new LzsMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    IsCompressed = is_compressed,
                    UnpackedSize = header.ToInt32 (8),
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LzsMetaData)info;
            using (var input = OpenLzss (file, meta.UnpackedSize, meta.IsCompressed))
                return Bmp.Read (input, info);
        }

        IBinaryStream OpenLzss (IBinaryStream file, int unpacked_size, bool is_compressed)
        {
            if (is_compressed)
            {
                var output = new byte[unpacked_size];
                file.Position = 0x10;
                Decompress (file, output);
                return new BinMemoryStream (output, 12, unpacked_size-12, file.Name);
            }
            else
            {
                var bmp = new StreamRegion (file.AsStream, 0x1C, true);
                return new BinaryStream (bmp, file.Name);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LzsFormat.Write not implemented");
        }

        void Decompress (IBinaryStream input, byte[] output)
        {
            int bit_mask = 0;
            int dst = 0;
            byte ctl_bits = 0;
            while (dst < output.Length)
            {
                bit_mask >>= 1;
                if (0 == bit_mask)
                {
                    ctl_bits = input.ReadUInt8();
                    bit_mask = 0x80;
                }
                if (0 != (bit_mask & ctl_bits))
                {
                    output[dst++] = input.ReadUInt8();
                }
                else
                {
                    int next = input.ReadUInt16();
                    int offset = next >> 4;
                    int count = next & 0xF;
                    if (0 == offset)
                    {
                        if (0xF == count)
                            count = input.ReadUInt8() + 0x1F;
                        else
                            count += 0x10;
    
                        count = Math.Min (count, output.Length - dst);
                        input.Read (output, dst, count);
                    }
                    else
                    {
                        if (0xF == count)
                            count = input.ReadUInt8() + 0x12;
                        else
                            count += 3;

                        count = Math.Min (count, output.Length - dst);
                        Binary.CopyOverlapped (output, dst - offset, dst, count);
                    }
                    dst += count;
                }
            }
        }
    }
}
