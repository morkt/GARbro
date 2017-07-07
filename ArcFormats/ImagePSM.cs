//! \file       ImagePSM.cs
//! \date       Fri Jul 07 19:16:57 2017
//! \brief      Obfuscated PNG image.
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
    public class PsmFormat : PngFormat
    {
        public override string         Tag { get { return "PSM"; } }
        public override string Description { get { return "Obfuscated PNG image"; } }
        public override uint     Signature { get { return 0x474E50ED; } }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var png = DeobfuscateStream (file))
                return base.Read (png, info);
        }

        IBinaryStream DeobfuscateStream (IBinaryStream file)
        {
            var header = file.ReadHeader (4).ToArray();
            header[0] = 0x89;
            var body = new StreamRegion (file.AsStream, 4, true);
            var png = new PrefixStream (header, body);
            return new BinaryStream (png, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            var start_pos = file.Position;
            base.Write (file, image);
            var end_pos = file.Position;
            file.Position = start_pos;
            file.WriteByte (0xED);
            file.Position = end_pos;
        }
    }
}
