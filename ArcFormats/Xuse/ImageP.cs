//! \file       ImageP.cs
//! \date       2022 May 07
//! \brief      Obfuscated PNG file.
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

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ImageFormat))]
    public class P4AGFormat : ImageFormat
    {
        public override string         Tag { get { return "P/4AG"; } }
        public override string Description { get { return "Xuse/Eternal obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x0A0D474E; } } // 'NG\n\r'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            using (var input = OpenAsPng (file))
                return Png.ReadMetaData (input);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var input = OpenAsPng (file))
                return Png.Read (input, info);
        }

        static readonly byte[] HeaderBytes = new byte[2] { PngFormat.HeaderBytes[0], PngFormat.HeaderBytes[1] };

        internal IBinaryStream OpenAsPng (IBinaryStream file)
        {
            var input = new PrefixStream (HeaderBytes, file.AsStream, true);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("P4AGFormat.Write not implemented");
        }
    }
}
