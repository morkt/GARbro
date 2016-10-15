//! \file       ImageKSL.cs
//! \date       Wed Aug 31 11:18:38 2016
//! \brief      KScript engine grayscale image format.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.KScript
{
    internal class KslMetaData : ImageMetaData
    {
        public byte Key;
        public int  DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class KslFormat : ImageFormat
    {
        public override string         Tag { get { return "KSL"; } }
        public override string Description { get { return "KScript grayscale image format"; } }
        public override uint     Signature { get { return 0x4D4C534B; } } // 'KSLM'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x14);
            return new KslMetaData
            {
                Width   = header.ToUInt32 (0xC),
                Height  = header.ToUInt32 (0x10),
                BPP     = 8,
                Key     = (byte)(header[4] ^ header[5]),
                DataLength = header.ToInt32 (8),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (KslMetaData)info;
            stream.Position = 0x14;
            var pixels = stream.ReadBytes (meta.DataLength);
            for (int i = 0; i < pixels.Length; ++i)
                pixels[i] ^= meta.Key;
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KslFormat.Write not implemented");
        }
    }
}
