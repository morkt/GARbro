//! \file       ImageTRI.cs
//! \date       2018 Jul 22
//! \brief      Triangle image format.
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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Triangle
{
    internal class TriMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class TriFormat : ImageFormat
    {
        public override string         Tag { get { return "TRI"; } }
        public override string Description { get { return "Triangle image format"; } }
        public override uint     Signature { get { return 0x7A495254; } } // 'TRIz'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            var bmp_header = new byte[56];
            Unpack (file, bmp_header);
            using (var bmp = new BinMemoryStream (bmp_header, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (bmp);
                if (null == bmp_info)
                    return null;
                return new TriMetaData {
                    Width = bmp_info.Width,
                    Height = bmp_info.Height,
                    BPP = bmp_info.BPP,
                    UnpackedSize = header.ToInt32 (4) ^ 0x65641538,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (TriMetaData)info;
            var output = new byte[meta.UnpackedSize];
            file.Position = 8;
            Unpack (file, output);
            using (var bmp = new BinMemoryStream (output, file.Name))
            {
                var decoder = new BmpBitmapDecoder (bmp, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("TriFormat.Write not implemented");
        }

        void Unpack (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            byte key = 0x7F;
            byte prev_key = 0;
            uint ctl = 0;
            while (dst < output.Length)
            {
                uint bit = ctl & 0x80000000;
                ctl <<= 1;
                if (0 == ctl)
                {
                    ctl = input.ReadUInt32();
                    bit = ctl & 0x80000000;
                    ctl <<= 1;
                }
                if (0 == bit)
                {
                    prev_key = key;
                    key ^= input.ReadUInt8();
                    output[dst++] = key;
                }
                else
                {
                    int offset = input.ReadUInt16();
                    offset += (int)ctl;
                    int count = (offset >> 12) & 0xF;
                    if (0 == count)
                    {
                        count = (prev_key + input.ReadUInt8()) & 0xFF;
                        if (0 == count)
                            break;
                        count += 15;
                    }
                    count = Math.Min (count + 2, output.Length - dst);
                    Binary.CopyOverlapped (output, dst + ~(offset & 0xFFF), dst, count);
                    dst += count;
                }
            }
        }
    }
}
