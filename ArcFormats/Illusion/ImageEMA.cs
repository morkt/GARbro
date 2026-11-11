//! \file       ImageEMA.cs
//! \date       2026-08-22
//! \brief      Illusion image format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.Illusion
{
    [Export(typeof(ImageFormat))]
    public class EmaBmpFormat : ImageFormat
    {
        public override string         Tag { get { return "EMA"; } }
        public override string Description { get { return "Illusion image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public EmaBmpFormat ()
        {
            Extensions = new string[] { "ema" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            uint width = stream.ReadUInt32();
            uint height = stream.ReadUInt32();
            var extension = stream.ReadBytes (5);
            if (!Binary.AsciiEqual (extension, ".bmp"))
                return null;
            var info = Bmp.ReadMetaData (OpenAsBitmap (stream));
            if (info.Width != width || info.Height != height)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0xD;
            return Bmp.Read (OpenAsBitmap (stream), info);
        }

        IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[] { (byte)'B', (byte)'M' };
            Stream stream = new StreamRegion (input.AsStream, input.Position + 2, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EmaFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class EmaTgaFormat : ImageFormat
    {
        public override string         Tag { get { return "EMA/TGA"; } }
        public override string Description { get { return "Illusion image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public EmaTgaFormat ()
        {
            Extensions = new string[] { "ema" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            uint width = stream.ReadUInt32();
            uint height = stream.ReadUInt32();
            var extension = stream.ReadBytes (5);
            if (!Binary.AsciiEqual (extension, ".tga"))
                return null;
            var info = Tga.ReadMetaData (stream);
            if (info.Width != width || info.Height != height)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0xD;
            var img = Tga.Read (stream, info).Bitmap;
            int bytes_per_pixel = info.BPP / 8;
            var pixels = new byte[info.iHeight * info.iWidth * bytes_per_pixel];
            img.CopyPixels (pixels, info.iWidth * bytes_per_pixel, 0);
            if (info.BPP == 32)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte r = pixels[i+3];
                    byte g = pixels[i+2];
                    byte b = pixels[i+1];
                    byte a = pixels[i];
                    pixels[i] = b;
                    pixels[i+1] = g;
                    pixels[i+2] = r;
                    pixels[i+3] = a;
                }
            }
            return ImageData.Create (info, img.Format, img.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("EmaFormat.Write not implemented");
        }
    }
}
