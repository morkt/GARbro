//! \file       ImageGPC.cs
//! \date       Fri Mar 24 03:31:55 2017
//! \brief      MicroVision image format.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.MicroVision
{
    internal class GpcMetaData : ImageMetaData
    {
        public  int Type;
    }

    [Export(typeof(ImageFormat))]
    public class GpcFormat : ImageFormat
    {
        public override string         Tag { get { return "GTX"; } }
        public override string Description { get { return "MicroVision image format"; } }
        public override uint     Signature { get { return 0x30435047; } } // 'GPC0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x40);
            int flags = header.ToUInt16 (0xC);
            if (0 != (flags & 0x1000))
            {
                var v1header = file.ReadBytes (0x30);
                return new GpcMetaData {
                    Width  = v1header.ToUInt16 (0x10),
                    Height = v1header.ToUInt16 (0x12),
                    BPP    = 32,
                    Type   = 0x1000
                };
            }
            else if (0 != (flags & 0x2000))
            {
                var v2header = file.ReadBytes (0x50);
                if (v2header.ToUInt16 (0x14) != 1)
                    return null;
                return new GpcMetaData {
                    Width  = v2header.ToUInt16 (0x10),
                    Height = v2header.ToUInt16 (0x12),
                    BPP    = 32,
                    Type   = 0x2000
                };
            }
            else
                return null;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GpcMetaData)info;
            if (0x2000 == meta.Type)
            {
                file.Position = 0x90;
                int stride = (int)meta.Width * 4;
                var pixels = new byte[stride * (int)meta.Height];
                if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                    throw new EndOfStreamException();
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, stride);
            }
            throw new NotImplementedException();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("GpcFormat.Write not implemented");
        }
    }
}
