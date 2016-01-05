//! \file       ImageANT.cs
//! \date       Mon Jan 04 05:07:49 2016
//! \brief      Studio e.go! bitmap.
//
// Copyright (C) 2015 by morkt
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

using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Ego
{
    [Export(typeof(ImageFormat))]
    public class AntFormat : ImageFormat
    {
        public override string         Tag { get { return "ANT"; } }
        public override string Description { get { return "Studio e.go! bitmap format"; } }
        public override uint     Signature { get { return 0x49544E41; } } // 'ANTI'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x18];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            return new ImageMetaData
            {
                Width   = LittleEndian.ToUInt32 (header, 0xC),
                Height  = LittleEndian.ToUInt32 (header, 0x10),
                BPP     = 32,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var pixels = new byte[info.Width*info.Height*4];
            stream.Position = 0x18;
            int dst = 0;
            for (uint y = 0; y < info.Height; ++y)
            {
                while (dst < pixels.Length)
                {
                    int a = stream.ReadByte();
                    if (-1 == a)
                        throw new EndOfStreamException();
                    else if (0 == a)
                    {
                        int count = stream.ReadByte();
                        if (-1 == count)
                            throw new EndOfStreamException();
                        else if (0 == count)
                            break;
                        dst += count * 4;
                    }
                    else
                    {
                        stream.Read (pixels, dst, 3);
                        pixels[dst + 3] = (byte)a;
                        dst += 4;
                    }
                }
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AntFormat.Write not implemented");
        }
    }
}
