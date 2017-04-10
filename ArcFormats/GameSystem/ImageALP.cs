//! \file       ImageALP.cs
//! \date       Thu Jan 19 21:19:47 2017
//! \brief      'Game System' grayscale image format.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ImageFormat))]
    public class AlpFormat : ImageFormat
    {
        public override string         Tag { get { return "ALP/GAMESYSTEM"; } }
        public override string Description { get { return "'Game System' grayscale image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".alp"))
                return null;
            var header = file.ReadHeader (8);
            uint width = header.ToUInt32 (0);
            uint height = header.ToUInt32 (4);
            if (0 == width || 0 == height)
                return null;
            if (file.Length != 8 + width * height)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 8;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AlpFormat.Write not implemented");
        }
    }
}
