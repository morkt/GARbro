//! \file       ImageMNV.cs
//! \date       Sat Apr 04 00:33:34 2015
//! \brief      M no Violet engine images implementation.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.MnoViolet
{
    internal class GraMetaData : ImageMetaData
    {
        public int PackedSize;
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag { get { return "GRA"; } }
        public override string Description { get { return "M no Violet image format"; } }
        public override uint     Signature { get { return 0x00617267; } } // 'gra'

        public GraFormat ()
        {
            Signatures = new uint[] { 0x00617267, 0x0073616d }; // 'gra', 'mas'
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GraFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            using (var input = new ArcView.Reader (stream))
            {
                uint sign   = input.ReadUInt32();
                uint width  = input.ReadUInt32();
                uint height = input.ReadUInt32();
                int packed_size = input.ReadInt32();
                int data_size   = input.ReadInt32();
                return new GraMetaData
                {
                    Width = width,
                    Height = height,
                    PackedSize = packed_size,
                    UnpackedSize = data_size,
                    BPP = 0x617267 == sign ? 24 : 8,
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = info as GraMetaData;
            if (null == meta)
                throw new ArgumentException ("GraFormat.Read should be supplied with GraMetaData", "info");

            stream.Position = 0x14;
            using (var reader = new LzssReader (stream, meta.PackedSize, meta.UnpackedSize))
            {
                reader.Unpack();
                int stride = (int)info.Width*info.BPP/8;
                var pixels = reader.Data;
                PixelFormat format;
                if (24 == info.BPP)
                    format = PixelFormats.Bgr24;
                else
                    format = PixelFormats.Gray8;
                var bitmap = BitmapSource.Create ((int)info.Width, (int)info.Height, 96, 96,
                                                  format, null, pixels, stride);
                var flipped = new TransformedBitmap (bitmap, new ScaleTransform { ScaleY = -1 });
                flipped.Freeze();
                return new ImageData (flipped, info);
            }
        }
    }
}
