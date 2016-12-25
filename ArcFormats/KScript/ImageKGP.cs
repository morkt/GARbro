//! \file       ImageKGP.cs
//! \date       Wed Aug 31 11:49:03 2016
//! \brief      KScript engine RGB image format.
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
using System.Security.Cryptography;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.KScript
{
    internal class KgpMetaData : ImageMetaData
    {
        public byte Key;
        public int  DataOffset;
        public int  DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class KgpFormat : ImageFormat
    {
        public override string         Tag { get { return "KGP"; } }
        public override string Description { get { return "KScript image format"; } }
        public override uint     Signature { get { return 0x48505247; } } // 'GRPH'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x1C);
            byte key = (byte)(header[4] ^ header[5]);
            int data_offset = 0x14;
            int x = 0, y = 0;
            if (0 != header[0xC])
            {
                data_offset += header.ToInt32 (0x10) / 0x10 * 0x18;
                x = header.ToInt32 (0x14);
                y = header.ToInt32 (0x18);
            }
            using (var input = new StreamRegion (stream.AsStream, data_offset, true))
            using (var crypto = new XoredStream (input, key))
            using (var png = new BinaryStream (crypto, stream.Name))
            {
                var info = Png.ReadMetaData (png);
                if (null == info)
                    return null;
                return new KgpMetaData
                {
                    Width   = info.Width,
                    Height  = info.Height,
                    OffsetX = x,
                    OffsetY = y,
                    BPP     = info.BPP,
                    Key     = key,
                    DataOffset = data_offset,
                    DataLength = header.ToInt32 (8),
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (KgpMetaData)info;
            using (var input = new StreamRegion (stream.AsStream, meta.DataOffset, true))
            using (var crypto = new XoredStream (input, meta.Key))
            using (var png = new BinaryStream (crypto, stream.Name))
                return Png.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("KgpFormat.Write not implemented");
        }
    }
}
