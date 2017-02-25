//! \file       ImageMAL.cs
//! \date       Sat Feb 25 05:13:05 2017
//! \brief      Valkyria mask image format.
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
using System.Windows.Media;

namespace GameRes.Formats.Valkyria
{
    [Export(typeof(ImageFormat))]
    public class MalFormat : ImageFormat
    {
        public override string         Tag { get { return "MAL"; } }
        public override string Description { get { return "Valkyria mask image format"; } }
        public override uint     Signature { get { return 0x4F43494D; } } // 'MICO'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0xE);
            if (!header.AsciiEqual (4, "MSK00"))
                return null;
            return new ImageMetaData 
            {
                Width = header.ToUInt16 (0xA),
                Height = header.ToUInt16 (0xC),
                BPP = 8
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0xE;
            int total = (int)info.Width * (int)info.Height;
            var pixels = new byte[total + 15];
            int dst = 0;
            while (dst < total)
            {
                int count = file.ReadUInt16();
                if (count > 0x7FFF)
                {
                    count &= 0x7FFF;
                    file.Read (pixels, dst, count);
                    dst += count;
                }
                else
                {
                    byte c = file.ReadUInt8();
                    while (count --> 0)
                        pixels[dst++] = c;
                }
            }
            return ImageData.Create (info, PixelFormats.Gray8, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MalFormat.Write not implemented");
        }
    }
}
