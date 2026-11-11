//! \file       ImageGS.cs
//! \date       Thu Apr 16 16:03:19 2015
//! \brief      GsPack image format implementation.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Gs
{
    internal class PicMetaData : ImageMetaData
    {
        public uint PackedSize;
        public uint UnpackedSize;
        public uint HeaderSize;
        public int  Extra;
    }

    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag { get { return "GsPIC"; } }
        public override string Description { get { return "GsPack image format"; } }
        public override uint     Signature { get { return 0x00040000; } }

        public PicFormat ()
        {
            Extensions = new string[] { "pic" }; // made-up
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            var info = new PicMetaData();
            info.PackedSize = file.ReadUInt32();
            info.UnpackedSize = file.ReadUInt32();
            info.HeaderSize = file.ReadUInt32();
            if (info.HeaderSize >= file.Length || info.PackedSize + info.HeaderSize > file.Length)
                return null;
            file.ReadUInt32();
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            info.BPP = file.ReadInt32();
            if (info.HeaderSize >= 0x2C)
            {
                info.Extra = file.ReadInt32();
                info.OffsetX = file.ReadInt32();
                info.OffsetY = file.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (PicMetaData)info;
            file.Position = meta.HeaderSize;
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                BitmapPalette palette = null;
                PixelFormat format;
                if (8 == meta.BPP) // read palette
                {
                    format = PixelFormats.Indexed8;
                    palette = ReadPalette (input);
                }
                else if (24 == meta.BPP)
                    format = PixelFormats.Bgr24;
                else if (16 == meta.BPP)
                    format = PixelFormats.Bgr565;
                else
                    format = PixelFormats.Bgr32;

                int stride = (int)meta.Width*((info.BPP+7)/8);
                var pixels = new byte[stride*meta.Height];
                input.Read (pixels, 0, pixels.Length);
                if (32 == meta.BPP && meta.Extra != 0)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        if (0 != pixels[i])
                        {
                            format = PixelFormats.Bgra32;
                            break;
                        }
                    }
                }
                return ImageData.Create (meta, format, palette, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PicFormat.Write not implemented");
        }
    }

    internal class GswinMetaData : ImageMetaData
    {
        public uint PackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class GswinGrpFormat : ImageFormat
    {
        public override string         Tag { get { return "GSWIN2"; } }
        public override string Description { get { return "GswSys 2.0 image format"; } }
        public override uint     Signature { get { return 0; } }

        public GswinGrpFormat ()
        {
            Extensions = new string[] { "pic" }; // same extension as the 5.0 pack image
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (file.Length < 0x28)
                return null;
            var info = new GswinMetaData { PackedSize = file.ReadUInt32 () };
            file.Position = 0x10;
            info.Width = file.ReadUInt32 ();
            info.Height = file.ReadUInt32 ();
            info.BPP = file.ReadInt32 ();
            if (0 == info.Width || 0 == info.Height || info.Width > 0x4000 || info.Height > 0x4000)
                return null;
            if (8 != info.BPP && 24 != info.BPP && 32 != info.BPP)
                return null;
            long unpacked_size = (long)info.Width*info.Height*(info.BPP/8) + (8 == info.BPP ? 0x400 : 0);
            long data_size = file.Length - 0x28;
            if (0 == info.PackedSize ? data_size != unpacked_size : info.PackedSize > data_size)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (GswinMetaData)info;
            file.Position = 0x28;
            if (0 != meta.PackedSize)
            {
                using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
                    return Decode (meta, input);
            }
            return Decode (meta, file.AsStream);
        }

        ImageData Decode (GswinMetaData meta, Stream input)
        {
            var pixels = new byte[meta.Width*meta.Height*4];
            if (8 == meta.BPP)
            {
                var palette = new byte[0x400];
                if (!ReadExactly (input, palette, palette.Length))
                    throw new InvalidFormatException ();
                bool has_alpha = false;
                for (int i = 3; i < palette.Length; i += 4)
                {
                    if (0 != palette[i])
                    {
                        has_alpha = true;
                        break;
                    }
                }
                var indices = new byte[meta.Width*meta.Height];
                if (!ReadExactly (input, indices, indices.Length))
                    throw new InvalidFormatException ();
                for (int i = 0; i < indices.Length; ++i)
                {
                    int src = indices[i]*4;
                    int dst = i*4;
                    pixels[dst  ] = palette[src  ];
                    pixels[dst+1] = palette[src+1];
                    pixels[dst+2] = palette[src+2];
                    pixels[dst+3] = has_alpha ? palette[src+3] : (byte)0xFF;
                }
            }
            else if (24 == meta.BPP)
            {
                var bgr = new byte[meta.Width*meta.Height*3];
                if (!ReadExactly (input, bgr, bgr.Length))
                    throw new InvalidFormatException ();
                for (int i = 0, src = 0, dst = 0; i < meta.Width*meta.Height; ++i, src += 3, dst += 4)
                {
                    pixels[dst  ] = bgr[src  ];
                    pixels[dst+1] = bgr[src+1];
                    pixels[dst+2] = bgr[src+2];
                    pixels[dst+3] = 0xFF;
                }
            }
            else
            {
                if (!ReadExactly (input, pixels, pixels.Length))
                    throw new InvalidFormatException ();
                bool has_alpha = false;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (0 != pixels[i])
                    {
                        has_alpha = true;
                        break;
                    }
                }
                if (!has_alpha)
                    for (int i = 3; i < pixels.Length; i += 4)
                        pixels[i] = 0xFF;
            }
            return ImageData.Create (meta, PixelFormats.Bgra32, null, pixels);
        }

        static bool ReadExactly (Stream input, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = input.Read (buffer, total, count-total);
                if (0 == read)
                    return false;
                total += read;
            }
            return true;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GswinGrpFormat.Write not implemented");
        }
    }
}
