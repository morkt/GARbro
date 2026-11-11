//! \file       ImagePXL.cs
//! \date       2026-04-14
//! \brief      Penguin Works image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.PenguinWorks {
    [Export(typeof(ImageFormat))]
    public class PxlFormat : ImageFormat {
        public override string         Tag { get { return "PXL"; } }
        public override string Description { get { return "Penguin Works image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData(IBinaryStream file) {
            uint width = file.ReadUInt32();
            uint height = file.ReadUInt32();
            if (file.Length != 8 + 3 * width * height)
                return null;
            return new ImageMetaData {
                Width  = width,
                Height = height,
                BPP    = 24,
            };
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info) {
            file.Position = 8;
            int stride = info.iWidth * 3;
            var pixels = new byte[stride * info.iHeight];
            file.Read(pixels, 0, pixels.Length);
            return ImageData.CreateFlipped(info, PixelFormats.Bgr24, null, pixels, stride);
        }

        public override void Write(Stream file, ImageData image) {
            throw new System.NotImplementedException("PxlFormat.Write not implemented");
        }
    }
}
