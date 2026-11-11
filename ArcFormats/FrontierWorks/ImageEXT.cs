//! \file       ImageEXT.cs
//! \date       Tue Feb 10 2026 08:21:40
//! \brief      Frontier Works engine image format.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.FrontierWorks
{
    [Export(typeof(ImageFormat))]
    public class ExtFormat : ImageFormat
    {
        public override string         Tag { get { return "EXT"; } }
        public override string Description { get { return "Frontier Works engine image format"; } }
        public override uint     Signature { get { return 0x30545845; } } // 'EXT0'

        public ExtFormat ()
        {
            Extensions = new string[] { "ext" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 0;
            var signature = stream.ReadInt32 ();
            if (0x30545845 != signature)
                return null;
            stream.Position = 0xC;
            var width = stream.ReadUInt32 ();
            var height = stream.ReadUInt32 ();
            if (width > 0x1000 || height > 0x1000)
                return null;
            stream.Position = 0x24;
            var bpp = stream.ReadByte ();
            if (32 != bpp)
                return null;
            return new ImageMetaData
            {
                Width = width,
                Height = height,
                BPP = bpp,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x100;
            var pixels = new byte[info.Width*info.Height*4];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException ();
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("ExtFormat.Write not implemented");
        }
    }
}
