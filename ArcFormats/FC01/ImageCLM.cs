//! \file       ImageCLM.cs
//! \date       Sun Dec 06 20:56:50 2015
//! \brief      F&C Co. image format.
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

using GameRes.Utility;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.FC01
{
    internal class ClmMetaData : ImageMetaData
    {
        public int  UnpackedSize;
        public uint DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class ClmFormat : ImageFormat
    {
        public override string         Tag { get { return "CLM"; } }
        public override string Description { get { return "F&C Co. image format"; } }
        public override uint     Signature { get { return 0x204D4C43; } } // 'CLM'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x40);
            if (!header.AsciiEqual (4, "1.00"))
                return null;
            uint data_offset = header.ToUInt32 (0x10);
            if (data_offset < 0x40)
                return null;
            uint width  = header.ToUInt32 (0x1C);
            uint height = header.ToUInt32 (0x20);
            int bpp = header.ToInt32 (0x24);
            int unpacked_size = header.ToInt32 (0x28);
            return new ClmMetaData
            {
                Width   = width,
                Height  = height,
                BPP     = bpp,
                UnpackedSize = unpacked_size,
                DataOffset = data_offset,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (ClmMetaData)info;
            stream.Position = meta.DataOffset;
            PixelFormat format;
            BitmapPalette palette = null;
            if (8 == meta.BPP)
            {
                format = PixelFormats.Indexed8;
                palette = ReadPalette (stream.AsStream);
            }
            else if (24 == meta.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == meta.BPP)
                format = PixelFormats.Bgr32;
            else
                throw new NotSupportedException ("Not supported CLM color depth");
            int packed_size = (int)(stream.Length - stream.Position);
            using (var reader = new MrgLzssReader (stream, packed_size, meta.UnpackedSize))
            {
                reader.Unpack();
                return ImageData.Create (info, format, palette, reader.Data);
            }
        }

        BitmapPalette ReadPalette (Stream input)
        {
            var palette_data = new byte[0x400];
            if (palette_data.Length != input.Read (palette_data, 0, palette_data.Length))
                throw new InvalidFormatException();
            var palette = new Color[0x100];
            for (int i = 0; i < palette.Length; ++i)
            {
                int c = i * 4;
                palette[i] = Color.FromRgb (palette_data[c+2], palette_data[c+1], palette_data[c]);
            }
            return new BitmapPalette (palette);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ClmFormat.Write not implemented");
        }
    }
}
