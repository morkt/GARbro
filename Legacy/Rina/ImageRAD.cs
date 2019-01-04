//! \file       ImageRAD.cs
//! \date       2019 Jan 01
//! \brief      Rina engine image format.
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

// [030620][Gipsy] Angel Gather

namespace GameRes.Formats.Rina
{
    internal class RadMetaData : ImageMetaData
    {
        public bool IsCompressed;
    }

    [Export(typeof(ImageFormat))]
    public class RadFormat : ImageFormat
    {
        public override string         Tag { get { return "RAD"; } }
        public override string Description { get { return "Rina engine image format"; } }
        public override uint     Signature { get { return 0x304152; } }

        public RadFormat ()
        {
            Signatures = new uint[] { 0x304152, 0x444152 }; // 'RA0', 'RAD'
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            return new RadMetaData {
                Width = 640, Height = 480, BPP = 24,
                IsCompressed = file.Signature == 0x304152,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (RadMetaData)info;
            file.Position = 4;
            int stride = info.iWidth * 3;
            var pixels = new byte[stride * info.iHeight];
            if (meta.IsCompressed)
                UnpackRgb (file, pixels);
            else
                file.Read (pixels, 0, pixels.Length);
            if (file.PeekByte() == -1)
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, stride);

            var alpha = new byte[info.iWidth * info.iHeight];
            if (meta.IsCompressed)
                UnpackAlpha (file, alpha);
            else
                file.Read (alpha, 0, alpha.Length);

            stride = info.iWidth * 4;
            var output = new byte[stride * info.iHeight];
            int src = 0;
            int asrc = 0;
            for (int dst = 0; dst < output.Length; dst += 4)
            {
                output[dst  ] = pixels[src++];
                output[dst+1] = pixels[src++];
                output[dst+2] = pixels[src++];
                output[dst+3] = alpha[asrc++];
            }
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, output, stride);
        }

        void UnpackRgb (IBinaryStream file, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                if (file.Read (output, dst, 3) != 3)
                    break;
                int count = 1;
                int pixel = output.ToInt24 (dst);
                if (0 == pixel)
                {
                    count = file.ReadUInt8();
                }
                dst += count * 3;
            }
        }

        void UnpackAlpha (IBinaryStream file, byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                int v = file.ReadByte();
                if (-1 == v)
                    break;
                int count = file.ReadByte();
                if (-1 == count)
                    break;
                count = System.Math.Min (count, output.Length - dst);
                while (count --> 0)
                    output[dst++] = (byte)v;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("RadFormat.Write not implemented");
        }
    }
}
