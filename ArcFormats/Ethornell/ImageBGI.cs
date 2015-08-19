//! \file       ImageBGI.cs
//! \date       Fri Apr 03 01:39:41 2015
//! \brief      BGI/Ethornell engine image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.BGI
{
    [Export(typeof(ImageFormat))]
    public class BgiFormat : ImageFormat
    {
        public override string         Tag { get { return "BGI"; } }
        public override string Description { get { return "BGI/Ethornell image format"; } }
        public override uint     Signature { get { return 0; } }

        public BgiFormat ()
        {
            Extensions = new string[] { "", "bgi" };
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("BgiFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                int width  = input.ReadInt16();
                int height = input.ReadInt16();
                if (width <= 0 || height <= 0)
                    return null;
                int bpp = input.ReadInt32();
                if (24 != bpp && 32 != bpp)
                    return null;
                if (0 != input.ReadInt64())
                    return null;
                return new ImageMetaData
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    BPP = bpp,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            int stride = (int)info.Width*((info.BPP+7)/8);
            var pixels = new byte[stride*info.Height];
            stream.Position = 0x10;
            int read = stream.Read (pixels, 0, pixels.Length);
            if (read != pixels.Length)
                throw new InvalidFormatException();
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else
                format = PixelFormats.Bgra32;
            var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                                              format, null, pixels, stride);
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }
    }
}
