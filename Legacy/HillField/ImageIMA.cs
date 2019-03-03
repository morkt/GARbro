//! \file       ImageIMA.cs
//! \date       2019 Mar 02
//! \brief      Hill Field script system image with alpha channel.
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

namespace GameRes.Formats.HillField
{
    internal class ImaMetaData : ImageMetaData
    {
        public uint AlphaOffset;
    }

    [Export(typeof(ImageFormat))]
    public class ImaFormat : ImageFormat
    {
        public override string         Tag { get { return "IMA"; } }
        public override string Description { get { return "Hill Field script system image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            if (header.ToInt32 (0) != 0)
                return null;
            uint rgb_size = header.ToUInt32 (4);
            uint width = header.ToUInt32 (8);
            uint height = header.ToUInt32 (12);
            uint plane_size = width * height;
            uint bitmap_size = plane_size * 3;
            if (plane_size == 0 || bitmap_size > rgb_size || file.Length - rgb_size - 8 < plane_size)
                return null;
            return new ImaMetaData {
                Width = width, Height = height, BPP = 32,
                AlphaOffset = 8 + rgb_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ImaMetaData)info;
            file.Position = 0x10;
            int plane_size = meta.iWidth * meta.iHeight;
            var rgb = file.ReadBytes (plane_size * 3);
            file.Position = meta.AlphaOffset;
            var alpha = file.ReadBytes (plane_size);

            int stride = meta.iWidth * 4;
            var pixels = new byte[stride * meta.iHeight];
            int dst = 0;
            int src = 0;
            int asrc = 0;
            while (dst < pixels.Length)
            {
                pixels[dst++] = rgb[src++];
                pixels[dst++] = rgb[src++];
                pixels[dst++] = rgb[src++];
                pixels[dst++] = (byte)~alpha[asrc++];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ImaFormat.Write not implemented");
        }
    }
}
