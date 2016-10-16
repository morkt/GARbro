//! \file       ImageCWL.cs
//! \date       Sat Jun 13 09:36:33 2015
//! \brief      Crowd compressed image format.
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
using System.Linq;
using System.Text;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ImageFormat))]
    public class CwdFormat : ImageFormat
    {
        public override string         Tag { get { return "CWD"; } }
        public override string Description { get { return "Crowd hi-color bitmap"; } }
        public override uint     Signature { get { return 0x20647763u; } } // 'cwd '

        static readonly byte[] SignatureText = Encoding.ASCII.GetBytes ("cwd format  - version 1.00 -");

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x38);
            if (!header.Take (SignatureText.Length).SequenceEqual (SignatureText))
                return null;
            uint key = header[0x34] + 0x259Au;
            return new ImageMetaData
            {
                Width  = header.ToUInt32 (0x2c) + key,
                Height = header.ToUInt32 (0x30) + key,
                BPP    = 15,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x38;
            int size = (int)info.Width * (int)info.Height * 2;
            var pixels = new byte[size];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            return ImageData.Create (info, PixelFormats.Bgr555, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("CwdFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class CwlFormat : CwdFormat
    {
        public override string         Tag { get { return "CWL"; } }
        public override string Description { get { return "LZ-compressed Crowd bitmap"; } }
        public override uint     Signature { get { return 0x44445A53u; } } // 'SZDD'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssReader (stream.AsStream, 100, 0x38)) // extract CWD header
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                using (var cwd = new BinMemoryStream (lz.Data, stream.Name))
                    return base.ReadMetaData (cwd);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            if (file.Length > int.MaxValue)
                throw new FileSizeException();
            var header = file.ReadHeader (14);
            int data_length = header.ToInt32 (10);
            int input_length = (int)(file.Length-file.Position);
            using (var lz = new LzssReader (file.AsStream, input_length, data_length))
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                using (var cwd = new BinMemoryStream (lz.Data, file.Name))
                    return base.Read (cwd, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("CwlFormat.Write not implemented");
        }
    }
}
