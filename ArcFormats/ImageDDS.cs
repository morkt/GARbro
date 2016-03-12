//! \file       ImageDDS.cs
//! \date       Thu Jul 23 18:12:05 2015
//! \brief      Direct Draw Surface image format.
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
using System.Text;
using System.Windows.Media;
using GameRes.Utility;

namespace GameRes.Formats.Microsoft
{
    internal class DdsMetaData : ImageMetaData
    {
        public int      DataOffset;
        public DdsPF    PixelFlags;
        public string   FourCC;
    }

    [Flags]
    internal enum DdsPF : uint
    {
        AlphaPixels     = 1,
        Alpha           = 2,
        FourCC          = 4,
        Rgb             = 0x40,
        Yuv             = 0x200,
        Luminance       = 0x20000,
    }

    [Export(typeof(ImageFormat))]
    public class DdsFormat : ImageFormat
    {
        public override string         Tag { get { return "DDS"; } }
        public override string Description { get { return "Direct Draw Surface format"; } }
        public override uint     Signature { get { return 0x20534444; } } // 'DDS'

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x5C];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            int dwSize = LittleEndian.ToInt32 (header, 4);
            if (dwSize < 0x7C)
                return null;
            var bitflags = (DdsPF)LittleEndian.ToUInt32 (header, 0x50);
            string four_cc = null;
            if (bitflags.HasFlag (DdsPF.FourCC))
                four_cc = Binary.GetCString (header, 0x54, 4, Encoding.ASCII);
            return new DdsMetaData
            {
                Width  = LittleEndian.ToUInt32 (header, 0x10),
                Height = LittleEndian.ToUInt32 (header, 0xC),
                BPP    = LittleEndian.ToInt32 (header, 0x58),
                PixelFlags = bitflags,
                FourCC = four_cc,
                DataOffset = 4 + dwSize,
            };
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (DdsMetaData)info;
            if (meta.PixelFlags.HasFlag (DdsPF.Yuv | DdsPF.Luminance))
                throw new NotSupportedException ("Not supported DDS texture color format");
            if (!string.IsNullOrEmpty (meta.FourCC))
                throw new NotImplementedException ("Compressed DDS textures not implemented");
            PixelFormat format;
            if (24 == info.BPP)
                format = PixelFormats.Bgr24;
            else if (16 == info.BPP)
                throw new NotImplementedException ("16bpp DDS textures not implemented");
            else if (meta.PixelFlags.HasFlag (DdsPF.AlphaPixels))
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Bgr32;
            stream.Position = meta.DataOffset;
            var pixels = new byte[info.Width*info.Height*((format.BitsPerPixel+7)/8)];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new InvalidFormatException ("Unexpected end of file");
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DdsFormat.Write not implemented");
        }
    }
}
