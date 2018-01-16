//! \file       ImageYPF.cs
//! \date       2017 Dec 12
//! \brief      Old image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Nabe
{
#if DEBUG
    [Export(typeof(ImageFormat))]
#endif
    public class YpfFormat : ImageFormat
    {
        public override string         Tag { get { return "YPF/NABE"; } }
        public override string Description { get { return "Studio Nabe Bugyou image format"; } }
        public override uint     Signature { get { return 0; } }

        public YpfFormat ()
        {
            Signatures = new uint[] { 0, 1, 2, 3 };
            Extensions = new string[] { "" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".ypf"))
                return null;
            var header = file.ReadHeader (0x10);
            if (header[0] != 1 && header[0] != 3)
                return null;
            uint w = header.ToUInt32 (4);
            uint h = header.ToUInt32 (8);
            if (0 == w || w > 0x8000 || 0 == h || h > 0x8000)
                return null;
            return new ImageMetaData {
                Width = w,
                Height = h,
                BPP = 1 == header[0] ? 24 : 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x10;
            int pixel_count = (int)info.Width * (int)info.Height;
            var pixels = file.ReadBytes (pixel_count * 3);
            if (32 == info.BPP)
            {
                var alpha = file.ReadBytes (pixel_count);
                var output = new byte[pixel_count * 4];
                int src = 0;
                int dst = 0;
                for (int i = 0; i < pixel_count; ++i)
                {
                    output[dst++] = pixels[src++];
                    output[dst++] = pixels[src++];
                    output[dst++] = pixels[src++];
                    output[dst++] = alpha[i];
                }
                pixels = output;
            }
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YpfFormat.Write not implemented");
        }
    }
}
