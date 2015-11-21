//! \file       ImageMGR.cs
//! \date       Sat Nov 21 01:58:44 2015
//! \brief      Propeller compressed bitmap.
//
// Copyright (C) 2015 by morkt
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
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Propeller
{
    internal class MgrMetaData : ImageMetaData
    {
        public int  Offset;
        public int  PackedSize;
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class MgrFormat : ImageFormat
    {
        public override string         Tag { get { return "MGR"; } }
        public override string Description { get { return "Propeller image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var reader = new ArcView.Reader (stream))
            {
                int count = reader.ReadInt16();
                if (count <= 0 || count >= 0x100)
                    return null;
                int offset;
                if (count > 1)
                {
                    offset = reader.ReadInt32();
                    if (offset != 2 + count * 4)
                        return null;
                }
                else
                    offset = 2;
                stream.Position = offset;
                int unpacked_size = reader.ReadInt32();
                int packed_size = reader.ReadInt32();
                offset += 8;
                if (offset + packed_size > stream.Length)
                    return null;
                byte[] header = new byte[0x36];
                if (0x36 != MgrOpener.Decompress (stream, header)
                    || header[0] != 'B' || header[1] != 'M')
                    return null;
                using (var bmp = new MemoryStream (header))
                {
                    var info = Bmp.ReadMetaData (bmp);
                    if (null == info)
                        return null;
                    return new MgrMetaData
                    {
                        Width = info.Width,
                        Height = info.Height,
                        BPP = info.BPP,
                        Offset = offset,
                        PackedSize = packed_size,
                        UnpackedSize = unpacked_size,
                    };
                }
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (MgrMetaData)info;
            stream.Position = meta.Offset;
            var data = new byte[meta.UnpackedSize];
            if (data.Length != MgrOpener.Decompress (stream, data))
                throw new InvalidFormatException();
            if (meta.BPP != 32)
            {
                using (var bmp = new MemoryStream (data))
                    return Bmp.Read (bmp, info);
            }
            // special case for 32bpp bitmaps with alpha-channel
            int stride = (int)meta.Width * 4;
            var pixels = new byte[stride * (int)meta.Height];
            int src = LittleEndian.ToInt32 (data, 0xA);
            for (int dst = stride*((int)meta.Height-1); dst >= 0; dst -= stride)
            {
                Buffer.BlockCopy (data, src, pixels, dst, stride);
                src += stride;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MgrFormat.Write not implemented");
        }
    }
}
