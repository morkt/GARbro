//! \file       ImageMGF.cs
//! \date       Thu Jun 25 06:08:32 2015
//! \brief      Malie System image format.
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
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.Malie
{
    [Export(typeof(ImageFormat))]
    public class MgfFormat : PngFormat
    {
        public override string         Tag { get { return "MGF"; } }
        public override string Description { get { return "Malie engine image format"; } }
        public override uint     Signature { get { return 0x696C614D; } } // 'Mali'
        public override bool      CanWrite { get { return true; } }

        static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (8).ToArray();
            if (!Binary.AsciiEqual (header, "MalieGF"))
                return null;
            Buffer.BlockCopy (PngHeader, 0, header, 0, 8);

            using (var data = new StreamRegion (stream.AsStream, 8, true))
            using (var pre = new PrefixStream (header, data))
            using (var png = new BinaryStream (pre, stream.Name))
                return base.ReadMetaData (png);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var header = PngHeader.Clone() as byte[];
            using (var data = new StreamRegion (stream.AsStream, 8, true))
            using (var pre = new PrefixStream (header, data))
            using (var png = new BinaryStream (pre, stream.Name))
                return base.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var png = new MemoryStream())
            {
                base.Write (png, image);
                var buffer = png.GetBuffer();
                Encoding.ASCII.GetBytes ("MalieGF\0", 0, 8, buffer, 0);
                file.Write (buffer, 0, (int)png.Length);
            }
        }
    }
}
