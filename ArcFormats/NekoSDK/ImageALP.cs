//! \file       ImageALP.cs
//! \date       Tue Aug 02 15:35:56 2016
//! \brief      NekoSDK BMP alpha-channel extension.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.NekoSDK
{
    [Export(typeof(IBmpExtension))]
    public class AlpBitmap : IBmpExtension
    {
        public ImageData Read (IBinaryStream file, BmpMetaData info)
        {
            if (!file.CanSeek)
                return null;
            var alp_name = Path.ChangeExtension (info.FileName, "alp");
            if (alp_name.Equals (info.FileName, StringComparison.InvariantCultureIgnoreCase)
                || !VFS.FileExists (alp_name))
                return null;
            int alp_stride = ((int)info.Width + 3) & -4;
            int alpha_size = alp_stride * (int)info.Height;
            var alpha = new byte[alpha_size];
            using (var alp = VFS.OpenStream (alp_name))
            {
                alpha_size = alp.Read (alpha, 0, alpha.Length);
                if (alp.ReadByte() != -1)
                    return null;
                if (alpha_size != alpha.Length)
                {
                    if (alp_stride == (int)info.Width)
                        return null;
                    alp_stride = (int)info.Width;
                    if (alpha_size != alp_stride * (int)info.Height)
                        return null;
                }
            }
            if (info.BPP != 24 && info.BPP != 32)
                return ReadBmp (file, info, alpha, alp_stride);

            file.Position = info.ImageOffset;
            int dst_stride = (int)info.Width * 4;
            int gap = -((int)info.Width * info.BPP / 8) & 3;
            var pixels = new byte[(int)info.Height * dst_stride];
            int src_pixel_size = info.BPP / 8;
            int dst = (int)(info.Height-1) * dst_stride;
            for (int y = (int)info.Height-1; y >= 0; --y)
            {
                int a_src = alp_stride * y;
                for (int x = 0; x < dst_stride; x += 4)
                {
                    file.Read (pixels, dst+x, src_pixel_size);
                    pixels[dst+x+3] = alpha[a_src++];
                }
                if (gap != 0)
                    file.Seek (gap, SeekOrigin.Current);
                dst -= dst_stride;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }

        public ImageData ReadBmp (IBinaryStream file, BmpMetaData info, byte[] alpha, int alp_stride)
        {
            var decoder = new BmpBitmapDecoder (file.AsStream,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);
            int dst_stride = (int)info.Width * 4;
            var pixels = new byte[(int)info.Height * dst_stride];
            bitmap.CopyPixels (pixels, dst_stride, 0);
            int dst = 0;
            int src = 0;
            for (int y = (int)info.Height; y > 0; --y)
            {
                int a_src = src;
                for (int x = 3; x < dst_stride; x += 4)
                {
                    pixels[dst+x] = alpha[a_src++];
                }
                dst += dst_stride;
                src += alp_stride;
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }
    }
}
