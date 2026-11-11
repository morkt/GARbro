//! \file       ImagePIZ.cs
//! \date       2025-12-24
//! \brief      ADVRUN compressed bitmap.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.AdvRun
{
    [Export(typeof(ImageFormat))]
    public class PizFormat : ImageFormat
    {
        public override string         Tag { get { return "PIZ"; } }
        public override string Description { get { return "ADVRUN compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public PizFormat ()
        {
            Extensions = new string[] { "piz" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (stream.Signature + 4 != stream.Length)
                return null;
            stream.Position = 4;
            using (var lz = new ZLibStream (stream.AsStream, CompressionMode.Decompress))
            {
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.ReadMetaData (bmp);
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            using (var lz = new ZLibStream (stream.AsStream, CompressionMode.Decompress))
            {
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.Read (bmp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PizFormat.Write not implemented");
        }
    }
}
