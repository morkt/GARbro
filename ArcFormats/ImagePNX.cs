//! \file       ImagePNX.cs
//! \date       2017 Nov 21
//! \brief      Encrypted PNG image.
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

using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Misc
{
    [Export(typeof(ImageFormat))]
    public class PnxFormat : PngFormat
    {
        public override string         Tag { get { return "PNX"; } }
        public override string Description { get { return "Encrypted PNG image"; } }
        public override uint     Signature { get { return 0x2F2638E1; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DeobfuscateStream (file, GuessEncryptionKey (file)))
                return base.Read (input, info);
        }

        byte GuessEncryptionKey (IBinaryStream file)
        {
            return (byte)(file.Signature ^ 0x89);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file, byte key)
        {
            var png = new XoredStream (file.AsStream, key, true);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PnxFormat.Write not implemented");
        }
    }
}
