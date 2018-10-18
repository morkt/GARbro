//! \file       ImageHBM.cs
//! \date       2018 Oct 18
//! \brief      Rits engine image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.Rits
{
    internal class HbmMetaData : ImageMetaData
    {
        public bool IsCompressed;
        public bool IsFlipped;
    }

    [Export(typeof(ImageFormat))]
    public class HbmFormat : ImageFormat
    {
        public override string         Tag { get { return "HBM"; } }
        public override string Description { get { return "Rit's image format"; } }
        public override uint     Signature { get { return 0x4D4248; } } // 'HBM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new HbmMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 16,
                IsCompressed = (header[12] & 0x10) != 0,
                IsFlipped    = (header[12] & 0x20) != 0,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (HbmMetaData)info;
            int stride = (2 * (int)meta.Width + 3) & ~3;
            var pixels = new byte[stride * (int)meta.Height];
            Stream input = file.AsStream;
            if (meta.IsCompressed)
            {
                input.Position = 0x14;
                input = new ZLibStream (input, CompressionMode.Decompress, true);
            }
            else
                input.Position = 0x10;
            if (meta.IsFlipped)
            {
                for (int dst = pixels.Length - stride; dst >= 0; dst -= stride)
                    input.Read (pixels, dst, stride);
            }
            else
                input.Read (pixels, 0, pixels.Length);
            if (input != file.AsStream)
                input.Dispose();
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HbmFormat.Write not implemented");
        }
    }
}
