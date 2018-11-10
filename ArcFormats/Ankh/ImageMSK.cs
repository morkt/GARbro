//! \file       ImageMSK.cs
//! \date       2018 Aug 25
//! \brief      Ankh bitmap format.
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

namespace GameRes.Formats.Ankh
{
    [Export(typeof(ImageFormat))]
    public class MskFormat : ImageFormat
    {
        public override string         Tag { get { return "MSK/ANKH"; } }
        public override string Description { get { return "Ankh bitmap format"; } }
        public override uint     Signature { get { return 0x6B736D; } } // 'msk'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            return new GpdMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 8,
                HeaderSize = header.ToInt32 (12) != 0 ? 12 : 16,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpdMetaData)info;
            file.Position = meta.HeaderSize;
            using (var lzss = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                var pixels = new byte[info.Width * info.Height];
                lzss.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MskFormat.Write not implemented");
        }
    }
}
