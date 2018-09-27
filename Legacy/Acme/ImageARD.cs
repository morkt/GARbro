//! \file       ImageARD.cs
//! \date       2018 May 24
//! \brief      Acme bitmap format.
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

// [031212][Acme] Hageshiku Botebara! Senpai, Watashi no Ko, Mitomete Kudasai!!
// [050325][Acme] Project Sex Shuudan Ninshin ~Athlete o Haramasero!~

namespace GameRes.Formats.Acme
{
    [Export(typeof(ImageFormat))]
    public class ArdFormat : ImageFormat
    {
        public override string         Tag { get { return "ARD"; } }
        public override string Description { get { return "Acme image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("ARD") || file.Length != 0x12C000)
                return null;
            return new ImageMetaData { Width = 640, Height = 480, BPP = 32 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var pixels = file.ReadBytes (0x12C000);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i];
                pixels[i  ] = pixels[i+1];
                pixels[i+1] = pixels[i+2];
                pixels[i+2] = pixels[i+3];
                pixels[i+3] = a;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ArdFormat.Write not implemented");
        }
    }
}
