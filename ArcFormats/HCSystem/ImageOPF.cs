//! \file       ImageOPF.cs
//! \date       2018 Jan 30
//! \brief      hcsystem image format.
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
using System.Windows.Media;

namespace GameRes.Formats.HCSystem
{
    internal class OpfMetaData : ImageMetaData
    {
        public int  Stride;
        public int  DataOffset;
        public int  DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class OpfFormat : ImageFormat
    {
        public override string         Tag { get { return "OPF"; } }
        public override string Description { get { return "hcsystem engine image format"; } }
        public override uint     Signature { get { return 0x2046504F; } } // 'OPF '

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            var info = new OpfMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = header.ToInt32 (0xC),
                Stride = header.ToInt32 (0x10),
                DataOffset = header.ToInt32 (0x14),
                DataLength = header.ToInt32 (0x18),
            };
            if (info.BPP > 32 || info.DataOffset < 32)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (OpfMetaData)info;
            file.Position = meta.DataOffset;
            PixelFormat format;
            switch (meta.BPP)
            {
            case 24: format = PixelFormats.Bgr24; break;
            case 32: format = PixelFormats.Bgra32; break;
            default: throw new InvalidFormatException ("Not supported OPF color depth.");
            }
            var pixels = file.ReadBytes (meta.DataLength);
            return ImageData.Create (info, format, null, pixels, meta.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("OpfFormat.Write not implemented");
        }
    }
}
