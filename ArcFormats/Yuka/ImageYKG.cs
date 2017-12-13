//! \file       ImageYKG.cs
//! \date       Thu Aug 13 22:16:30 2015
//! \brief      Yuka engine images implementation.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Yuka
{
    internal class YkgMetaData : ImageMetaData
    {
        public uint     DataOffset;
        public uint     DataSize;
        public YkgImage Format;
    }

    internal enum YkgImage
    {
        Bmp, Png, Gnp,
    }

    [Export(typeof(ImageFormat))]
    public class YkgFormat : ImageFormat
    {
        public override string         Tag { get { return "YKG"; } }
        public override string Description { get { return "Yuka engine image format"; } }
        public override uint     Signature { get { return 0x30474B59; } } // 'YKG0'

        static readonly byte[] PngPrefix = new byte[4] { 0x89, 'P'^0, 'N'^0, 'G'^0 };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            if (!header.AsciiEqual (4, "00\0\0"))
                return null;
            var ykg = new YkgMetaData {
                DataOffset = header.ToUInt32 (0x28),
                DataSize   = header.ToUInt32 (0x2C)
            };
            if (0 == ykg.DataOffset)
                ykg.DataOffset = header.ToUInt32 (8);
            if (ykg.DataOffset < 0x30)
                return null;
            if (0 == ykg.DataSize)
                ykg.DataSize = (uint)(file.Length - ykg.DataOffset);
            ImageMetaData info = null;
            using (var reg = new StreamRegion (file.AsStream, ykg.DataOffset, ykg.DataSize, true))
            using (var img = new BinaryStream (reg, file.Name))
            {
                var img_header = img.ReadHeader (4);
                if (img_header.AsciiEqual ("BM"))
                {
                    img.Position = 0;
                    info = Bmp.ReadMetaData (img);
                    ykg.Format = YkgImage.Bmp;
                }
                else if (img_header.AsciiEqual ("\x89PNG"))
                {
                    img.Position = 0;
                    info = Png.ReadMetaData (img);
                    ykg.Format = YkgImage.Png;
                }
                else if (img_header.AsciiEqual ("\x89GNP"))
                {
                    using (var body = new StreamRegion (file.AsStream, ykg.DataOffset+4, ykg.DataSize-4, true))
                    using (var pre = new PrefixStream (PngPrefix, body))
                    using (var png = new BinaryStream (pre, file.Name))
                        info = Png.ReadMetaData (png);
                    ykg.Format = YkgImage.Gnp;
                }
            }
            if (null == info)
                return null;
            ykg.Width = info.Width;
            ykg.Height = info.Height;
            ykg.BPP = info.BPP;
            ykg.OffsetX = info.OffsetX;
            ykg.OffsetY = info.OffsetY;
            return ykg;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (YkgMetaData)info;

            switch (meta.Format)
            {
            case YkgImage.Bmp:
                using (var reg = new StreamRegion (stream.AsStream, meta.DataOffset, meta.DataSize, true))
                using (var bmp = new BinaryStream (reg, stream.Name))
                    return Bmp.Read (bmp, info);
            case YkgImage.Png:
                using (var reg = new StreamRegion (stream.AsStream, meta.DataOffset, meta.DataSize, true))
                using (var png = new BinaryStream (reg, stream.Name))
                    return Png.Read (png, info);
            case YkgImage.Gnp:
                using (var body = new StreamRegion (stream.AsStream, meta.DataOffset+4, meta.DataSize-4, true))
                using (var pre = new PrefixStream (PngPrefix, body))
                using (var png = new BinaryStream (pre, stream.Name))
                    return Png.Read (png, info);
            default:
                throw new InvalidFormatException();
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YkgFormat.Write not implemented");
        }
    }
}
