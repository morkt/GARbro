//! \file       ImageABM.cs
//! \date       Tue Aug 04 22:58:17 2015
//! \brief      LiLiM/Le.Chocolat compressed image format.
//
// Copyright (C) 2015-2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Lilim
{
    [Export(typeof(ImageFormat))]
    public class AbmFormat : ImageFormat
    {
        public override string         Tag { get { return "ABM"; } }
        public override string Description { get { return "LiLiM/Le.Chocolat compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x46);
            if ('B' != header[0] || 'M' != header[1])
                return null;
            int type = header.ToInt16 (0x1C);
            uint frame_offset;
            int bpp = 24;
            if (1 == type || 2 == type)
            {
                int count = header.ToUInt16 (0x3A);
                if (count > 0xFF)
                    return null;
                frame_offset = header.ToUInt32 (0x42);
            }
            else if (32 == type || 24 == type || 8 == type || -8 == type)
            {
                uint unpacked_size = header.ToUInt32 (2);
                if (0 == unpacked_size || unpacked_size == stream.Length) // probably an ordinary bmp file
                    return null;
                frame_offset = header.ToUInt32 (0xA);
                if (8 == type)
                    bpp = 8;
            }
            else
                return null;
            if (frame_offset >= stream.Length)
                return null;
            return new AbmImageData
            {
                Width = header.ToUInt32 (0x12),
                Height = header.ToUInt32 (0x16),
                BPP = bpp,
                Mode = type,
                BaseOffset = frame_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var reader = new AbmReader (stream, (AbmImageData)info))
                return reader.Image;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("AbmFormat.Write not implemented");
        }
    }
}
