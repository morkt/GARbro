//! \file       ImageJPEG.cs
//! \date       Thu Jul 17 15:56:27 2014
//! \brief      JPEG image implementation.
//
// Copyright (C) 2014 by morkt
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
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    public class JpegFormat : ImageFormat
    {
        public override string Tag { get { return "JPEG"; } }
        public override string Description { get { return "JPEG image file format"; } }
        public override uint Signature { get { return 0; } }

        public int Quality { get; set; }

        public JpegFormat ()
        {
            Extensions = new string[] { "jpg", "jpeg" };
            Signatures = new uint[] { 0xe0ffd8ffu, 0 };
            Quality = 90;
        }

        public override ImageData Read (Stream file, ImageMetaData info)
        {
            var decoder = new JpegBitmapDecoder (file,
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return new ImageData (frame, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            var encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = Quality;
            encoder.Frames.Add (BitmapFrame.Create (image.Bitmap));
            encoder.Save (file);
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            if (0xff != stream.ReadByte() || 0xd8 != stream.ReadByte())
                return null;
            using (var file = new ArcView.Reader (stream))
            {
                while (-1 != file.PeekChar())
                {
                    ushort marker = Binary.BigEndian (file.ReadUInt16());
                    if ((marker & 0xff00) != 0xff00)
                        break;
                    int length = Binary.BigEndian (file.ReadUInt16());
                    if ((marker & 0x00f0) == 0xc0 && marker != 0xffc4)
                    {
                        if (length < 8)
                            break;
                        int bits = file.ReadByte();
                        uint height = Binary.BigEndian (file.ReadUInt16());
                        uint width  = Binary.BigEndian (file.ReadUInt16());
                        int components = file.ReadByte();
                        return new ImageMetaData {
                            Width = width,
                            Height = height,
                            BPP = bits * components,
                        };
                    }
                    file.BaseStream.Seek (length-2, SeekOrigin.Current);
                }
                return null;
            }
        }
    }
}
