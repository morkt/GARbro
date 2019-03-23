//! \file       ImageBBM.cs
//! \date       2019 Mar 20
//! \brief      BlueGale obfuscated bitmap.
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.BlueGale
{
    [Export(typeof(ImageFormat))]
    public class BbmFormat : ImageFormat
    {
        public override string         Tag { get { return "BBM"; } }
        public override string Description { get { return "BlueGale obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x20);
            if (header.ToUInt16 (0) != 0xB2BD || header.ToUInt32 (6) != 0xFFFFFFFF)
                return null;
            for (int i = 0; i < header.Length; ++i)
                header[i] ^= 0xFF;
            if (header.ToInt32 (0xE) != 0x28)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt16 (0x12),
                Height = header.ToUInt16 (0x16),
                BPP    = header.ToUInt16 (0x1C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var header = file.ReadHeader (100).ToArray();
            for (int i = 0; i < header.Length; ++i)
                header[i] ^= 0xFF;
            using (var rest = new StreamRegion (file.AsStream, 100, true))
            using (var input = new PrefixStream (header, rest))
            {
                var decoder = new BmpBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BbmFormat.Write not implemented");
        }
    }
}
