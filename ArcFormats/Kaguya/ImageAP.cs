//! \file       ImageAP.cs
//! \date       Mon Jun 01 09:22:41 2015
//! \brief      KaGuYa script engine bitmap format.
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
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ImageFormat))]
    public class ApFormat : ImageFormat
    {
        public override string         Tag { get { return "AP"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public ApFormat ()
        {
            Extensions = new string[] { "bg_", "cg_", "cgw", "sp_", "aps", "alp", "prs" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int A = stream.ReadByte();
            int P = stream.ReadByte();
            if ('A' != A || 'P' != P)
                return null;
            var info = new ImageMetaData();
            info.Width = stream.ReadUInt32();
            info.Height = stream.ReadUInt32();
            info.BPP = stream.ReadInt16();
            if (info.Width > 0x8000 || info.Height > 0x8000 || !(32 == info.BPP || 24 == info.BPP))
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 12;
            return ReadBitmapData (stream, info);
        }

        protected ImageData ReadBitmapData (IBinaryStream stream, ImageMetaData info)
        {
            int stride = (int)info.Width*4;
            var pixels = new byte[stride*info.Height];
            for (int row = (int)info.Height-1; row >= 0; --row)
            {
                if (stride != stream.Read (pixels, row*stride, stride))
                    throw new InvalidFormatException();
            }
            PixelFormat format = PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new BinaryWriter (file, Encoding.ASCII, true))
            {
                output.Write ((byte)'A');
                output.Write ((byte)'P');
                output.Write (image.Width);
                output.Write (image.Height);
                output.Write ((short)24);
                WriteBitmapData (file, image);
            }
        }

        protected void WriteBitmapData (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
            {
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
            }
            int stride = (int)image.Width * 4;
            byte[] row_data = new byte[stride];
            Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
            for (uint row = 0; row < image.Height; ++row)
            {
                --rect.Y;
                bitmap.CopyPixels (rect, row_data, stride, 0);
                file.Write (row_data, 0, row_data.Length);
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class Ap2Format : ImageFormat
    {
        public override string         Tag { get { return "AP-2"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0x322D5041; } } // 'AP-2'

        public Ap2Format ()
        {
            Extensions = new string[] { "alp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            var info = new ImageMetaData();
            info.OffsetX = stream.ReadInt32();
            info.OffsetY = stream.ReadInt32();
            info.Width   = stream.ReadUInt32();
            info.Height  = stream.ReadUInt32();
            info.BPP = 32;
            if (info.Width > 0x8000 || info.Height > 0x8000)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x18;
            var pixels = new byte[4*info.Width*info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, 4*(int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ap2Format.Write not implemented");
        }
    }
}
