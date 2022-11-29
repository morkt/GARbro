//! \file       ImageALP.cs
//! \date       2022 Apr 22
//! \brief      BeF bitmap mask
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

namespace GameRes.Formats.Hmp
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", -1)]
    public class AlpFormat : ImageFormat
    {
        public override string         Tag { get { return "ALP/BeF"; } }
        public override string Description { get { return "BeF bitmap mask format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".alp") || file.Length != 0x25800)
                return null;
            return new ImageMetaData { Width = 320, Height = 480, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = file.ReadBytes (0x25800);
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] = (byte)(pixels[i] * 0xFF / 0x40);
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlpFormat.Write not implemented");
        }
    }
}
