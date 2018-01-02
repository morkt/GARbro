//! \file       ImageGEF.cs
//! \date       2018 Jan 02
//! \brief      PNG-embedded image.
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

namespace GameRes.Formats.YellowCap
{
    [Export(typeof(ImageFormat))]
    public class GefFormat : ImageFormat
    {
        public override string         Tag { get { return "GEF"; } }
        public override string Description { get { return "PNG-embedded image format"; } }
        public override uint     Signature { get { return 0x00010100; } }

        public GefFormat ()
        {
            Signatures = new uint[] { 0x00010100, 0xFF010100 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (header.ToUInt32 (0xC) != Png.Signature)
                return null;
            file.Position = 0xC;
            var info = Png.ReadMetaData (file);
            if (null == info || info.Width != header.ToUInt32 (4) || info.Height != header.ToUInt32 (8))
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var part = new StreamRegion (file.AsStream, 0xC, true))
            using (var png = new BinaryStream (part, file.Name))
                return Png.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GefFormat.Write not implemented");
        }
    }
}
