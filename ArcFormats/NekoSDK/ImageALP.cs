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

namespace GameRes.Formats.NekoSDK
{
    [Export(typeof(IBmpExtension))]
    public class AlpBitmap : IBmpExtension
    {
        public ImageData Read (IBinaryStream file, BmpMetaData info)
        {
            if (info.BPP != 24 && info.BPP != 32 || !file.CanSeek)
                return null;
            var alp_name = Path.ChangeExtension (info.FileName, "alp");
            if (alp_name.Equals (info.FileName, StringComparison.InvariantCultureIgnoreCase)
                || !VFS.FileExists (alp_name))
                return null;
            var alpha_size = info.Width * info.Height;
            var alpha = new byte[alpha_size];
            using (var alp = VFS.OpenStream (alp_name))
            {
                if (alpha.Length != alp.Read (alpha, 0, alpha.Length) || alp.ReadByte() != -1)
                    return null;
            }
            file.Position = info.HeaderLength;
            int dst_stride = (int)info.Width * 4;
            int gap = -((int)info.Width * info.BPP / 8) & 3;
            var pixels = new byte[(int)info.Height * dst_stride];
            int src_pixel_size = info.BPP / 8;
            int dst = (int)(info.Height-1) * dst_stride;
            for (int y = (int)info.Height-1; y >= 0; --y)
            {
                int a_src = (int)info.Width * y;
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
    }
}
