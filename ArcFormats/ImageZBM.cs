//! \file       ImageZBM.cs
//! \date       Thu Jun 11 16:24:09 2015
//! \brief      LZ-compressed bitmap format.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Runtime.InteropServices;
using GameRes.Utility;

namespace GameRes.Formats.Crowd
{
    [Export(typeof(ImageFormat))]
    public class ZbmFormat : BmpFormat
    {
        public override string         Tag { get { return "ZBM"; } }
        public override string Description { get { return "LZ-compressed bitmap"; } }
        public override uint     Signature { get { return 0x44445A53u; } } // 'SZDD'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 0x0e;
            using (var lz = new LzssReader (stream, 100, 54)) // extract BMP header
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                var header = lz.Data;
                for (int i = 0; i < 54; ++i)
                    header[i] ^= 0xff;
                using (var bmp = new MemoryStream (header))
                    return base.ReadMetaData (bmp);
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            if (stream.Length > int.MaxValue)
                throw new FileSizeException();
            var header = new byte[14];
            if (header.Length != stream.Read (header, 0, header.Length))
                throw new InvalidFormatException();
            int data_length = LittleEndian.ToInt32 (header, 10);
            int input_length = (int)(stream.Length-stream.Position);
            using (var lz = new LzssReader (stream, input_length, data_length))
            {
                lz.FrameSize = 0x1000;
                lz.FrameFill = 0x20;
                lz.FrameInitPos = 0x1000 - 0x10;
                lz.Unpack();
                var data = lz.Data;
                int count = Math.Min (100, data.Length);
                for (int i = 0; i < count; ++i)
                    data[i] ^= 0xff;
                using (var bmp = new MemoryStream (data))
                    return base.Read (bmp, info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("ZbmFormat.Write not implemented");
        }
    }
}
