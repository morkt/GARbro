//! \file       ImageSJ1.cs
//! \date       2026-07-14
//! \brief      Scenario-Kun image format.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.ScenarioKun {
    [Export(typeof(ImageFormat))]
    public class Sj1Format : ImageFormat {
        public override string         Tag { get { return "SJ1"; } }
        public override string Description { get { return "Scenario-Kun image format"; } }
        public override uint     Signature { get { return 0x859ABD9A; } }
        public override bool      CanWrite { get { return false; } }

        public Sj1Format() {
            Extensions = new string[] { "sj1" };
        }

        public override ImageMetaData ReadMetaData(IBinaryStream stream) {
            stream = BinaryStream.FromStream(new XoredStream(stream.AsStream, 0x65), stream.Name);
            return Jpeg.ReadMetaData(stream);
        }

        public override ImageData Read(IBinaryStream stream, ImageMetaData info) {
            stream = BinaryStream.FromStream(new XoredStream(stream.AsStream, 0x65), stream.Name);
            return Jpeg.Read(stream, info);
        }

        public override void Write(Stream file, ImageData image) {
            throw new NotImplementedException("Sj1Format.Write not implemented");
        }
    }
}
