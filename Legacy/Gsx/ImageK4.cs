//! \file       ImageK4.cs
//! \date       2019 Feb 07
//! \brief      Toyo GSX image format.
//
// Copyright (C) 2019 by morkt
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

// [030825][Mirai] Hoshi no Oujo

namespace GameRes.Formats.Gsx
{
    internal class K4MetaData : ImageMetaData
    {
        public bool HasAlpha;
        public int  FrameCount;
    }

    [Export(typeof(ImageFormat))]
    public class K4Format : ImageFormat
    {
        public override string         Tag { get { return "K4"; } }
        public override string Description { get { return "Toyo GSX image format"; } }
        public override uint     Signature { get { return 0x0201344B; } } // 'K4'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (!header.AsciiEqual ("K4"))
                return null;
            if (header[2] != 1 || header[3] != 2)
                return null;
            return new K4MetaData {
                Width  = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP    = header[0xF],
                HasAlpha = header[0xB] != 0,
                FrameCount = header.ToUInt16 (0xC),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (K4MetaData)info;

            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("K4Format.Write not implemented");
        }
    }
}
