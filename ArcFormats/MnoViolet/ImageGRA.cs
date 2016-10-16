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
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.MnoViolet
{
    internal class GraMetaData : ImageMetaData
    {
        public int PackedSize;
        public int UnpackedSize;
        public long DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class GraFormat : ImageFormat
    {
        public override string         Tag { get { return "GRA"; } }
        public override string Description { get { return "M no Violet image format"; } }
        public override uint     Signature { get { return 0x00617267; } } // 'gra'

        public GraFormat ()
        {
            Signatures = new uint[] { 0x00617267, 0x0073616d, 0 }; // 'gra', 'mas'
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("GraFormat.Write not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            uint sign   = file.ReadUInt32();
            uint width  = file.ReadUInt32();
            uint height = file.ReadUInt32();
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            int bpp;
            if (0x617267 == sign) // 'gra'
                bpp = 24;
            else if (0x73616D == sign) // 'mas'
                bpp = 8;
            else if (1 == sign)
                bpp = file.ReadInt32();
            else
                return null;
            if (bpp != 32 && bpp != 24 && bpp != 8)
                return null;
            int packed_size = file.ReadInt32();
            int data_size   = file.ReadInt32();
            return new GraMetaData
            {
                Width = width,
                Height = height,
                PackedSize = packed_size,
                UnpackedSize = data_size,
                BPP = bpp,
                DataOffset = file.Position,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (GraMetaData)info;
            stream.Position = meta.DataOffset;
            using (var reader = new LzssReader (stream.AsStream, meta.PackedSize, meta.UnpackedSize))
            {
                reader.Unpack();
                int stride = ((int)info.Width*info.BPP/8 + 3) & ~3;
                PixelFormat format;
                if (32 == info.BPP)
                    format = PixelFormats.Bgra32;
                else if (24 == info.BPP)
                    format = PixelFormats.Bgr24;
                else
                    format = PixelFormats.Gray8;
                return ImageData.CreateFlipped (info, format, null, reader.Data, stride);
            }
        }
    }
}
