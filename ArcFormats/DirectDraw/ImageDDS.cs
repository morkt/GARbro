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
        public uint     RBitMask;
        public uint     GBitMask;
        public uint     BBitMask;
        public uint     ABitMask;
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

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x6C);
            int dwSize = header.ToInt32 (4);
            if (dwSize < 0x7C)
                return null;
            var bitflags = (DdsPF)header.ToUInt32 (0x50);
            string four_cc = null;
            if (bitflags.HasFlag (DdsPF.FourCC))
                four_cc = Binary.GetCString (header.ToArray(), 0x54, 4, Encoding.ASCII);
            return new DdsMetaData
            {
                Width  = header.ToUInt32 (0x10),
                Height = header.ToUInt32 (0xC),
                BPP    = header.ToInt32 (0x58),
                PixelFlags = bitflags,
                FourCC = four_cc,
                RBitMask = header.ToUInt32 (0x5C),
                GBitMask = header.ToUInt32 (0x60),
                BBitMask = header.ToUInt32 (0x64),
                ABitMask = header.ToUInt32 (0x68),
                DataOffset = 4 + dwSize,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DdsMetaData)info;
            if (meta.PixelFlags.HasFlag (DdsPF.Yuv | DdsPF.Luminance))
                throw new NotSupportedException ("Not supported DDS texture color format");
            if (!string.IsNullOrEmpty (meta.FourCC))
                throw new NotImplementedException ("Compressed DDS textures not implemented");
            if (meta.PixelFlags.HasFlag (DdsPF.Rgb)
                && (0 == meta.RBitMask || 0 == meta.GBitMask || 0 == meta.BBitMask))
                throw new InvalidFormatException();
            var pixels = ReadPixelData (stream.AsStream, meta);
            PixelFormat format;
            if (meta.PixelFlags.HasFlag (DdsPF.AlphaPixels) && meta.ABitMask != 0)
                format = PixelFormats.Bgra32;
            else
                format = PixelFormats.Bgr32;
            return ImageData.Create (info, format, null, pixels);
        }

        byte[] ReadPixelData (Stream stream, DdsMetaData info)
        {
            int src_pixel_size = (info.BPP+7) / 8;
            int input_size = (int)info.Width*(int)info.Height*src_pixel_size;
            var input = new byte[input_size+4];
            stream.Position = info.DataOffset;
            if (input_size != stream.Read (input, 0, input_size))
                throw new InvalidFormatException ("Unexpected end of file");
            if (32 == info.BPP && 0xFF0000 == info.RBitMask && 0x00FF00 == info.GBitMask && 0x0000FF == info.BBitMask)
                return input;
            var output = new byte[info.Width*info.Height*4];
            int dst = 0;
            Func<int, uint> get_pixel;
            if (8 == info.BPP)
                get_pixel = x => input[x];
            else if (info.BPP <= 16)
                get_pixel = x => LittleEndian.ToUInt16 (input, x);
            else
                get_pixel = x => LittleEndian.ToUInt32 (input, x);
            Func<uint, uint, byte> convert_pixel;
            if (info.BPP > 24)
                convert_pixel = (p, mask) => (byte)((p & mask) * 0xFFL / mask);
            else
                convert_pixel = (p, mask) => (byte)((p & mask) * 0xFFu / mask);
            bool has_alpha = info.PixelFlags.HasFlag (DdsPF.AlphaPixels) && info.ABitMask != 0;
            for (int src = 0; src < input_size; src += src_pixel_size)
            {
                uint src_pixel = get_pixel (src);
                output[dst++] = convert_pixel (src_pixel, info.BBitMask);
                output[dst++] = convert_pixel (src_pixel, info.GBitMask);
                output[dst++] = convert_pixel (src_pixel, info.RBitMask);
                if (has_alpha)
                    output[dst] = convert_pixel (src_pixel, info.ABitMask);
                dst++;
            }
            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DdsFormat.Write not implemented");
        }
    }
}
