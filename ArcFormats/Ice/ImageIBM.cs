//! \file       ImageIBM.cs
//! \date       2023 Aug 25
//! \brief      Ice Soft compressed bitmap format.
//
// Copyright (C) 2023 by morkt
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
using System.Windows.Media.Imaging;
using GameRes.Formats.Ankh;

// [000324][Juice] Orgel ~Kesenai Melody~

namespace GameRes.Formats.Ice
{
    internal class IbmMetaData : ImageMetaData
    {
        public int UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class IbmFormat : ImageFormat
    {
        public override string         Tag { get => "IBM/ICE"; }
        public override string Description { get => "Ice Soft compressed bitmap"; }
        public override uint     Signature { get => 0x01575054; } // 'TPW'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int unpacked_size = file.ReadInt32();
            if (unpacked_size <= 0)
                return null;
            var bmp_header = new byte[56];
            GrpOpener.UnpackTpw (file, bmp_header);
            using (var bmp = new BinMemoryStream (bmp_header, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (bmp);
                if (null == bmp_info)
                    return null;
                return new IbmMetaData {
                    Width  = bmp_info.Width,
                    Height = bmp_info.Height,
                    BPP    = bmp_info.BPP,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (IbmMetaData)info;
            var output = new byte[meta.UnpackedSize];
            GrpOpener.UnpackTpw (file, output);
            using (var bmp = new BinMemoryStream (output, file.Name))
            {
                var decoder = new BmpBitmapDecoder (bmp, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IbmFormat.Write not implemented");
        }
    }
}
