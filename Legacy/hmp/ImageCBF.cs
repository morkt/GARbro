//! \file       ImageCBF.cs
//! \date       2022 May 19
//! \brief      h.m.p image format.
//
// Copyright (C) 2022 by morkt
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

// [990521][Sepia] Hikari no Naka de Dakishimete

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    public class CbfFormat : ImageFormat
    {
        public override string         Tag { get { return "CBF/MA"; } }
        public override string Description { get { return "h.m.p image format"; } }
        public override uint     Signature { get { return 0x432D414D; } } // 'MA-CBF'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1C);
            if (!header.AsciiEqual ("MA-CBF"))
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0x14),
                BPP    = 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x24;
            int stride = info.iWidth * 2;
            var pixels = new byte[stride * info.iHeight];
            file.Read (pixels, 0, pixels.Length);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CbfFormat.Write not implemented");
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "NE")]
    [ExportMetadata("Target", "WAV")]
    public class NeFormat : ResourceAlias { }
}
