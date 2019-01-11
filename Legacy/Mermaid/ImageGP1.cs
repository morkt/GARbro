//! \file       ImageGP1.cs
//! \date       2019 Jan 10
//! \brief      Mermaid image format.
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media;

// [030314][Mermaid] Ayakashizoushi ~Oumagatoki no Yume~

namespace GameRes.Formats.Mermaid
{
    [Export(typeof(ImageFormat))]
    public class Gp1Format : ImageFormat
    {
        public override string         Tag { get { return "GP1"; } }
        public override string Description { get { return "Mermaid image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("GP1"))
                return null;
            var header = file.ReadHeader (8);
            uint width  = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            return new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 24,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            int plane_size = info.iWidth * info.iHeight;
            var b = new byte[plane_size];
            UnpackChannel (file, b);
            var g = new byte[plane_size];
            UnpackChannel (file, g);
            var r = new byte[plane_size];
            UnpackChannel (file, r);
            var pixels = new byte[plane_size * 3];
            int dst = 0;
            for (int src = 0; src < plane_size; ++src)
            {
                pixels[dst++] = b[src];
                pixels[dst++] = g[src];
                pixels[dst++] = r[src];
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Gp1Format.Write not implemented");
        }

        void UnpackChannel (IBinaryStream input, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int count = input.ReadByte();
                if (count < 0)
                    break;
                if (count <= 0x32)
                {
                    input.Read (output, dst, count);
                    dst += count;
                }
                else
                {
                    count -= 0x32;
                    byte v = input.ReadUInt8();
                    while (count --> 0)
                        output[dst++] = v;
                }
            }
        }
    }
}
