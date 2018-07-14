//! \file       ImageMWP.cs
//! \date       Sun Aug 30 02:15:38 2015
//! \brief      Emic engine RGBA bitmap.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Emic
{
    [Export(typeof(ImageFormat))]
    public class MwpFormat : ImageFormat
    {
        public override string         Tag { get { return "MWP"; } }
        public override string Description { get { return "Emic engine bitmap"; } }
        public override uint     Signature { get { return 0x1050574D; } } // 'MWP\x10'

        public MwpFormat ()
        {
            Extensions = new string[] { "mwp", "bmp" };
            Signatures = new uint[] { 0x1050574D, 0x4C594554 }; // 'TEYL'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            return new ImageMetaData
            {
                Width  = file.ReadUInt32(),
                Height = file.ReadUInt32(),
                BPP = 32,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = new byte[info.Width*info.Height*4];
            file.Position = 12;
            if (pixels.Length != file.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MwpFormat.Write not implemented");
        }
    }
}
