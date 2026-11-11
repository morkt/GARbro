//! \file       ImageLZS.cs
//! \date       2026-08-18
//! \brief      LZSS-compressed bitmap.
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
using GameRes.Utility;

namespace GameRes.Formats.Artifact
{
    [Export(typeof(ImageFormat))]
    public class LzsFormat : Misc.LzsFormat
    {
        public override string         Tag { get { return "LZS/MOO"; } }
        public override string Description { get { return "LZSS-compressed bitmap"; } }
        public override uint     Signature { get { return 0x53535A4C; } } // 'LZSS'
        public override bool      CanWrite { get { return false; } }

        protected override IBinaryStream OpenLzss (IBinaryStream file, int unpacked_size, bool is_compressed)
        {
            if (is_compressed)
            {
                var output = new byte[unpacked_size];
                file.Position = 0x10;
                Decompress (file, output);
                return new BinMemoryStream (output, 0, unpacked_size, file.Name);
            }
            else
            {
                var bmp = new StreamRegion (file.AsStream, 0x10, true);
                return new BinaryStream (bmp, file.Name);
            }
        }
    }
}
