//! \file       ImagePNG.cs
//! \date       2023 Sep 02
//! \brief      PNG image with inverted alpha-channel.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Ism
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)] // deprioritize
    public class PngIsmFormat : ImageFormat
    {
        public override string         Tag { get => "PNG/ISM"; }
        public override string Description { get => "ISM engine PNG image"; }
        public override uint     Signature { get => 0x474e5089; }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            // format only applied when extracting from related archive
            if (!VFS.IsVirtual || VFS.CurrentArchive.Tag != "ISA")
                return null;
            return Png.ReadMetaData (file);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var decoder = new PngBitmapDecoder (file.AsStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            if (bitmap.Format != PixelFormats.Bgra32)
                return new ImageData (bitmap, info);
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels (pixels, stride, 0);
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] ^= 0xFF;
            }
            return ImageData.Create (info, bitmap.Format, bitmap.Palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PngIsmFormat.Write not implemented");
        }
    }
}
