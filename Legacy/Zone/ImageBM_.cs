//! \file       ImageBM_.cs
//! \date       2019 Jun 25
//! \brief      Zone compressed image.
//
// Copyright (C) 2019 by morkt
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
using System.Windows.Media;

namespace GameRes.Formats.Zone
{
    internal class Bm_MetaData : ImageMetaData
    {
        public bool IsCompressed;
        public byte RleFlag;
    }

    [Export(typeof(ImageFormat))]
    public class Bm_Format : ImageFormat
    {
        public override string         Tag { get { return "BM_/ZONE"; } }
        public override string Description { get { return "Zone compressed image"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".bm_"))
                return null;
            var header = file.ReadHeader (0x18);
            int signature = header.ToInt32 (0);
            if (signature != 0 && signature != 1)
                return null;
            file.Position = 0x418;
            return new Bm_MetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP    = 8,
                IsCompressed = signature != 0,
                RleFlag = file.ReadUInt8(),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Bm_MetaData)info;
            file.Position = 0x18;
            var palette = ReadPalette (file.AsStream);
            var pixels = new byte[info.Width * info.Height];
            file.Position = 0x424;
            if (meta.IsCompressed)
                ReadRle (file, pixels, meta.RleFlag);
            else
                file.Read (pixels, 0, pixels.Length);
            if (meta.IsCompressed)
                return ImageData.Create (info, PixelFormats.Indexed8, palette, pixels);
            else
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, info.iWidth);
        }

        void ReadRle (IBinaryStream input, byte[] output, byte rle_flag)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                byte b = input.ReadUInt8();
                if (b != rle_flag)
                {
                    output[dst++] = b;
                    continue;
                }
                b = input.ReadUInt8();
                if (0 == b)
                {
                    output[dst++] = rle_flag;
                    continue;
                }
                int count = 0;
                while (1 == b)
                {
                    count += 0x100;
                    b = input.ReadUInt8();
                }
                count += b;
                if (b != 0)
                    input.ReadByte();
                b = input.ReadUInt8();
                while (count --> 0)
                    output[dst++] = b;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Bm_Format.Write not implemented");
        }
    }
}
