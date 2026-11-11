//! \file       ImageNekoPNG.cs
//! \date       Sun Nov 19 15:40:46 2023
//! \brief      NekoNyan PNG image format.
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
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace GameRes.Formats.Artemis
{
    internal class NekoPNGMetaData : ImageMetaData
    {
        public int[] Offset;
        public int[] Length;
    }

    [Export(typeof(ImageFormat))]
    public class ImageNekoPNG : ImageFormat
    {
        public override string         Tag { get { return "PNG/NekoNyan"; } }
        public override string Description { get { return "NekoNyan PNG image format"; } }
        public override uint     Signature { get { return 0x00000040; } }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var metadata = (NekoPNGMetaData)info;
            var bitmap = new WriteableBitmap ((int)info.Width, (int)info.Height, ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Bgra32, null);
            bitmap.Lock ();
            try
            {
                var output = bitmap.BackBuffer;
                int stride = bitmap.BackBufferStride;
                for (var i = 0; i < 8; i++)
                {
                    file.Position = metadata.Offset[i];
                    var data = file.ReadBytes (metadata.Length[i]);
                    for (var j = 0; j < 16; j++)
                        data[j] -= (byte)(0x77 * j);
                    var slice_width = 0;
                    var slice_height = 0;
                    if (1 != WebPCodec.WebPGetInfo(data, (UIntPtr)data.Length, ref slice_width, ref slice_height))
                        throw new InvalidFormatException("WebP image decoder failed.");
                    var slice_size = stride * slice_height;
                    if (IntPtr.Zero == WebPCodec.WebPDecodeBGRAInto (data, (UIntPtr)data.Length, output, (UIntPtr)slice_size, stride))
                        throw new InvalidFormatException("WebP image decoder failed.");
                    output += slice_size;
                }
                bitmap.AddDirtyRect (new Int32Rect(0, 0, (int)info.Width, (int)info.Height));
            }
            finally
            {
                bitmap.Unlock();
            }
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Signature != 0x40)
                return null;
            if (file.Length <= 0x40)
                return null;
            var offset = new int[8];
            var length = new int[8];
            for (var i = 0; i < 8; i++)
                offset[i] = file.ReadInt32();
            for (var i = 0; i < 8; i++)
                length[i] = file.ReadInt32();
            WebPCodec.Load();
            var image_width = 0;
            var image_height = 0;
            for (var i = 0; i < 8; i++)
            {
                if (offset[i] < 64)
                    return null;
                if (length[i] < 32)
                    return null;
                if (offset[i] + length[i] > file.Length)
                    return null;
                file.Position = offset[i];
                var webp_header = file.ReadBytes (32);
                for (var j = 0; j < 16; j++)
                    webp_header[j] -= (byte)(0x77 * j);
                var slice_width = 0;
                var slice_height = 0;
                if (1 != WebPCodec.WebPGetInfo (webp_header, (UIntPtr)webp_header.Length, ref slice_width, ref slice_height))
                    throw new InvalidFormatException ("WebP image decoder failed.");
                image_width = Math.Max (image_width, slice_width);
                image_height += slice_height;
            }
            return new NekoPNGMetaData
            {
                Width = (uint)image_width,
                Height = (uint)image_height,
                BPP = 32,
                Offset = offset,
                Length = length,
            };
        }

        public override void Write (Stream file, ImageData bitmap)
        {
            throw new NotImplementedException("ImageNekoPNG.Write not implemented");
        }
    }
}
