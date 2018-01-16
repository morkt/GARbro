//! \file       ImageIMG.cs
//! \date       2018 Jan 16
//! \brief      Lilim obfuscated bitmap.
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

namespace GameRes.Formats.Lilim
{
    public abstract class BaseImgFormat : ImageFormat
    {
        internal IBinaryStream DeobfuscateStream (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20).ToArray();
            for (int i = 0; i < 0x20; ++i)
                header[i] ^= 0xFF;
            Stream stream = new StreamRegion (file.AsStream, 0x20, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, file.Name);
        }
    }

    [Export(typeof(ImageFormat))]
    public class ImgBmpFormat : BaseImgFormat
    {
        public override string         Tag { get { return "IMG/BMP"; } }
        public override string Description { get { return "Lilim obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if ((header[0]^0xFF) != 'B' || (header[1]^0xFF) != 'M')
                return null;
            using (var bmp = DeobfuscateStream (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = DeobfuscateStream (file))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImgFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class ImgPngFormat : BaseImgFormat
    {
        public override string         Tag { get { return "IMG/PNG"; } }
        public override string Description { get { return "Lilim obfuscated image"; } }
        public override uint     Signature { get { return 0xB8B1AF76; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var png = DeobfuscateStream (file))
                return Png.ReadMetaData (png);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var png = DeobfuscateStream (file))
                return Png.Read (png, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImgFormat.Write not implemented");
        }
    }
}
