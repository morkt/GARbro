//! \file       ImageGRP.cs
//! \date       2023 Aug 16
//! \brief      Herb Soft compressed image format.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Herb
{
    internal class GrpMetaData : ImageMetaData
    {
        public int Stride;
    }

    [Export(typeof(ImageFormat))]
    public class GrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GRP/HERB"; } }
        public override string Description { get { return "Herb Soft image format"; } }
        public override uint     Signature { get { return 0x08; } }

        public GrpFormat ()
        {
            Signatures = new[] { 0x20u, 0x18u, 0x08u };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x28);
            if (header.ToInt32 (4) != 0 || header.ToInt32 (8) != 1)
                return null;
            return new GrpMetaData {
                Width  = header.ToUInt32 (0x20),
                Height = header.ToUInt32 (0x24),
                BPP    = header[0] == 0x08 ? 8 : header[0] == 0x18 ? 16 : 24,
                Stride = header.ToInt32 (0x0C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GrpMetaData)info;
            BitmapPalette palette = null;
            PixelFormat format =  8 == info.BPP ? PixelFormats.Indexed8
                               : 16 == info.BPP ? PixelFormats.Bgr555
                                                : PixelFormats.Bgr24;
            if (8 == info.BPP)
            {
                file.Position = 0x28;
                palette = ReadPalette (file.AsStream, 0x100, PaletteFormat.RgbX);
            }
            int stride = meta.Stride;
            var pixels = new byte[stride * info.iHeight];
            file.Position = 0x428;
            using (var input = new ZLibStream (file.AsStream, CompressionMode.Decompress, true))
            {
                input.Read (pixels, 0, pixels.Length);
            }
            return ImageData.Create (info, format, palette, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GrpFormat.Write not implemented");
        }
    }
}
