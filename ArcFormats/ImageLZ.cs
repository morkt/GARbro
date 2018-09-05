//! \file       ImageLZ.cs
//! \date       Thu Feb 18 14:40:40 2016
//! \brief      LZ-compressed bitmap.
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

namespace GameRes.Formats
{
    [Export(typeof(ImageFormat))]
    public class Bm_Format : ImageFormat
    {
        public override string         Tag { get { return "BM_"; } }
        public override string Description { get { return "LZ-compressed bitmap"; } }
        public override uint     Signature { get { return 0x44445A53u; } } // 'SZDD'
        public override bool      CanWrite { get { return false; } }

        public Bm_Format ()
        {
            Extensions = new string[] { "bm_", "gpp", "meh", "gr_" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                lz.Config.FrameSize = 0x1000;
                lz.Config.FrameFill = 0x20;
                lz.Config.FrameInitPos = 0x1000 - 0x10;
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.ReadMetaData (bmp);
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                lz.Config.FrameSize = 0x1000;
                lz.Config.FrameFill = 0x20;
                lz.Config.FrameInitPos = 0x1000 - 0x10;
                using (var bmp = new BinaryStream (lz, stream.Name))
                    return Bmp.Read (bmp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Bm_Format.Write not implemented");
        }
    }
}
