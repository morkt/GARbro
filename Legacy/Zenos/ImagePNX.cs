//! \file       ImagePNX.cs
//! \date       2022 Apr 14
//! \brief      Obfuscated PNG image.
//
// Copyright (C) 2022 by morkt
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

namespace GameRes.Formats.Zenos
{
    [Export(typeof(ImageFormat))]
    public class PnxFormat : PngFormat
    {
        public override string         Tag { get { return "PNX/ZENOS"; } }
        public override string Description { get { return "Obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x584E5089; } }
        public override bool      CanWrite { get { return false; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = DeobfuscateStream (file))
                return base.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = DeobfuscateStream (file))
                return base.Read (input, info);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file)
        {
            var body = new StreamRegion (file.AsStream, 8, file.Length-8, true);
            var png = new PrefixStream (HeaderBytes, body);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("PnxFormat.Write not implemented");
        }
    }
}
