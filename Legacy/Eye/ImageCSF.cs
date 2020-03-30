//! \file       ImageCSF.cs
//! \date       2019 May 28
//! \brief      Compressed bitmap format.
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
using GameRes.Compression;

namespace GameRes.Formats.Eye
{
    [Export(typeof(ImageFormat))]
    public class CsfFormat : ImageFormat
    {
        public override string         Tag { get { return "CSF"; } }
        public override string Description { get { return "Compressed bitmap format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xB);
            if (!header.AsciiEqual ("CSF"))
                return null;
            using (var input = UnpackedStream (file))
                return Bmp.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = UnpackedStream (file))
                return Bmp.Read (input, info);
        }

        IBinaryStream UnpackedStream (IBinaryStream input)
        {
            input.Position = 0xB;
            var unpacked = new LzssStream (input.AsStream, LzssMode.Decompress, true);
            return new BinaryStream (unpacked, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CsfFormat.Write not implemented");
        }
    }
}
