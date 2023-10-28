//! \file       ImageHTF.cs
//! \date       2023 Oct 07
//! \brief      Huffman-compressed bitmap.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Jam
{
    [Export(typeof(ImageFormat))]
    public class HtfFormat : ImageFormat
    {
        public override string         Tag => "HTF";
        public override string Description => "Huffman-compressed bitmap";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".HTF"))
                return null;
            int unpacked_size = file.ReadInt32();
            if (unpacked_size <= 0 || unpacked_size > 0x1000000)
                return null;
            using (var huff = new HuffmanStream (file.AsStream, true))
            using (var input = new BinaryStream (huff, file.Name))
            {
                return Bmp.ReadMetaData (input);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 4;
            using (var input = new HuffmanStream (file.AsStream, true))
            {
                var decoder = new BmpBitmapDecoder (input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return new ImageData (decoder.Frames[0], info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HtfFormat.Write not implemented");
        }
    }
}
