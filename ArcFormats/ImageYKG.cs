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

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x40];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            if (!Binary.AsciiEqual (header, 4, "00\0\0"))
                return null;
            var ykg = new YkgMetaData {
                DataOffset = LittleEndian.ToUInt32 (header, 0x28),
                DataSize   = LittleEndian.ToUInt32 (header, 0x2C)
            };
            if (ykg.DataOffset < 0x30)
                return null;
            ImageMetaData info = null;
            using (var img = new StreamRegion (stream, ykg.DataOffset, ykg.DataSize, true))
            {
                if (4 != img.Read (header, 0, 4))
                    return null;
                if (Binary.AsciiEqual (header, "BM"))
                {
                    img.Position = 0;
                    info = ImageFormat.Bmp.ReadMetaData (img);
                    ykg.Format = YkgImage.Bmp;
                }
                else if (Binary.AsciiEqual (header, "\x89PNG"))
                {
                    img.Position = 0;
                    info = Png.ReadMetaData (img);
                    ykg.Format = YkgImage.Png;
                }
                else if (Binary.AsciiEqual (header, "\x89GNP"))
                {
                    using (var body = new StreamRegion (stream, ykg.DataOffset+4, ykg.DataSize-4, true))
                    using (var png = new PrefixStream (PngPrefix, body))
                        info = Png.ReadMetaData (png);
                    ykg.Format = YkgImage.Gnp;
                }
                else
                {
                    return null;
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

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as YkgMetaData;
            if (null == meta)
                throw new ArgumentException ("YkgFormat.Read should be supplied with YkgMetaData", "info");

            switch (meta.Format)
            {
            case YkgImage.Bmp:
                using (var bmp = new StreamRegion (stream, meta.DataOffset, meta.DataSize, true))
                    return Bmp.Read (bmp, info);
            case YkgImage.Png:
                using (var png = new StreamRegion (stream, meta.DataOffset, meta.DataSize, true))
                    return Png.Read (png, info);
            case YkgImage.Gnp:
                using (var body = new StreamRegion (stream, meta.DataOffset+4, meta.DataSize-4, true))
                using (var png = new PrefixStream (PngPrefix, body))
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
