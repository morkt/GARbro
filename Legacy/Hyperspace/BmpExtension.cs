//! \file       BmpExtension.cs
//! \date       2018 Jan 14
//! \brief      BMP files with obfuscated color depth.
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
using System.Windows.Media;

namespace GameRes.Formats.Hyperspace
{
    /// <summary>
    /// Color depth of 24bpp images simply changed to 16bpp. This extensions reverts it back.
    /// </summary>
    [Export(typeof(IBmpExtension))]
    public class BmpDepthFixer : IBmpExtension
    {
        public ImageData Read (IBinaryStream file, BmpMetaData info)
        {
            if (info.BPP != 0x10 || !file.CanSeek)
                return null;
            int stride = ((int)info.Width * 3 + 3) & ~3;
            int total_24bpp = stride * (int)info.Height;
            if (total_24bpp + info.ImageOffset != file.Length)
                return null;
            file.Position = info.ImageOffset;
            var pixels = file.ReadBytes ((int)total_24bpp);
            return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);
        }
    }
}
