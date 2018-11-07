//! \file       ImageBJR.cs
//! \date       2018 Nov 06
//! \brief      Leaf obfuscated bitmap.
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

namespace GameRes.Formats.Leaf
{
    internal class BjrMetaData : BmpMetaData
    {
        public bool IsFlipped;
    }

    [Export(typeof(ImageFormat))]
    public class BjrFormat : ImageFormat
    {
        public override string         Tag { get { return "BJR"; } }
        public override string Description { get { return "Leaf obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bjr"))
                return null;
            var header = file.ReadHeader (0x36);
            if (!header.AsciiEqual ("BM"))
                return null;
            int height = header.ToInt32 (0x16);
            return new BjrMetaData {
                Width = header.ToUInt32 (0x12),
                Height = (uint)Math.Abs (height),
                BPP = header.ToUInt16 (0x1C),
                ImageLength = header.ToUInt32 (2),
                ImageOffset = header.ToUInt32 (0xA),
                IsFlipped = height > 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (BjrMetaData)info;
            int height = (int)meta.Height;
            int stride = ((int)meta.Width * (meta.BPP / 8) + 3) & ~3;
            file.Position = meta.ImageOffset;
            var source = file.ReadBytes (stride * height);

            var name_bytes = Encodings.cp932.GetBytes (Path.GetFileName (meta.FileName));
            int line_key = 0;
            int even_key = 0xFF;
            int odd_key  = 0;
            for (int i = 0; i < name_bytes.Length; ++i)
            {
                line_key ^= name_bytes[i];
                even_key += name_bytes[i];
                odd_key  -= name_bytes[i];
            }

            var pixels = new byte[source.Length];
            int dst = 0;
            for (int y = 0; y < height; ++y)
            {
                line_key += 7;
                int src = (line_key % height) * stride;
                for (int x = 0; x < stride; ++x)
                {
                    if ((x & 1) == 0)
                        pixels[dst+x] = (byte)(even_key - source[src+x]);
                    else
                        pixels[dst+x] = (byte)(odd_key  + source[src+x]);
                }
                dst += stride;
            }
            PixelFormat format;
            if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else // FIXME read palette
                format = PixelFormats.Gray8;
            if (meta.IsFlipped)
                return ImageData.CreateFlipped (info, format, null, pixels, stride);
            else
                return ImageData.Create (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BjrFormat.Write not implemented");
        }
    }
}
