//! \file       ImagePGA.cs
//! \date       Mon Oct 26 06:38:07 2015
//! \brief      Obfuscated PNG image.
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

using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Palette
{
    [Export(typeof(ImageFormat))]
    public class PgaFormat : PngFormat
    {
        public override string         Tag { get { return "PGA"; } }
        public override string Description { get { return "Palette obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x50414750; } } // 'PGAP'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var png = DeobfuscateStream (stream))
                return base.ReadMetaData (png);
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            using (var png = DeobfuscateStream (stream))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var png = new MemoryStream())
            {
                base.Write (png, image);
                var buffer = png.GetBuffer();
                for (int i = 0; i < 8; ++i)
                    buffer[i+8] ^= (byte)"PGAECODE"[i];
                buffer[5] = (byte)'P';
                buffer[6] = (byte)'G';
                buffer[7] = (byte)'A';
                file.Write (buffer, 5, (int)png.Length - 5);
            }
        }

        public static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0xD, 0xA, 0x1A, 0xA };
        public static readonly byte[] PngFooter = { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

        Stream DeobfuscateStream (Stream stream)
        {
            var png_header = new byte[0x10];
            stream.Read (png_header, 5, 11);
            System.Buffer.BlockCopy (PngHeader, 0, png_header, 0, 8);
            for (int i = 0; i < 8; ++i)
                png_header[i+8] ^= (byte)"PGAECODE"[i];
            var png_body = new StreamRegion (stream, 11, true);
            return new PrefixStream (png_header, png_body);
        }
    }
}
