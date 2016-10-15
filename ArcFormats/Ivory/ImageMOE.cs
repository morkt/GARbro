//! \file       ImageMOE.cs
//! \date       Thu Sep 08 17:56:51 2016
//! \brief      Ivory image format.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Ivory
{
    [Export(typeof(ImageFormat))]
    public class MoeFormat : ImageFormat
    {
        public override string         Tag { get { return "MOE"; } }
        public override string Description { get { return "Ivory image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var wh = stream.Signature;
            uint width  = wh & 0xFFFF;
            uint height = wh >> 16;
            if (0 == width || width > 800 || 0 == height || height > 600)
                return null;
            if (!IsValidInput (stream.AsStream, width, height))
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 24 };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 4;
            var pixels = new byte[3 * info.Width * info.Height];
            int dst = 0;
            while (dst < pixels.Length)
            {
                int count = stream.ReadByte();
                if (-1 == count)
                    throw new EndOfStreamException();
                if (0 != (count & 0x80))
                {
                    count = 3 * (count & 0x7F);
                    stream.Read (pixels, dst, count);
                    dst += count;
                }
                else
                {
                    count *= 3;
                    stream.Read (pixels, dst, 3);
                    Binary.CopyOverlapped (pixels, dst, dst+3, count-3);
                    dst += count;
                }
            }
            return ImageData.Create (info, PixelFormats.Bgr24, null, pixels);
        }

        /// <summary>
        /// Try to interpret input stream as a compressed image.
        /// </summary>
        bool IsValidInput (Stream input, uint width, uint height)
        {
            int total = (int)width * (int)height;
            int dst = 0;
            while (dst < total)
            {
                int count = input.ReadByte();
                if (-1 == count)
                    return false;
                if (0 != (count & 0x80))
                {
                    count &= 0x7F;
                    input.Seek (count * 3, SeekOrigin.Current);
                }
                else
                {
                    input.Seek (3, SeekOrigin.Current);
                }
                dst += count;
                if (dst > total)
                    return false;
            }
            return true;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("MoeFormat.Write not implemented");
        }
    }
}
