//! \file       ImageWM2.cs
//! \date       2018 Apr 07
//! \brief      F&C Co. bitmap mask format.
//
// Copyright (C) 2018 by morkt
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

namespace GameRes.Formats.FC01
{
    [Export(typeof(ImageFormat))]
    public class Wm2Format : ImageFormat
    {
        public override string         Tag { get { return "WM2"; } }
        public override string Description { get { return "F&C Co. bitmap mask"; } }
        public override uint     Signature { get { return 0x302E32; } } // '2.0'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (12);
            uint width  = header.ToUInt32 (4);
            uint height = header.ToUInt32 (8);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var output = new byte[info.Width * info.Height];
            var table = new int[info.Height * 4];
            file.Position = 12;
            for (int i = 0; i < table.Length; ++i)
                table[i] = file.ReadInt32();
            int stride = (int)info.Width;
            int height = (int)info.Height;
            int row = 0;
            int data_pos = height * 16 + 12;
            int dst = 0;
            for (int y = 0; y < height; ++y)
            {
                if (table[row] != 0)
                {
                    file.Position = table[row+3] + data_pos;
                    file.Read (output, dst+table[row+1], table[row+2]);
                }
                row += 4;
                dst += stride;
            }
            return ImageData.Create (info, PixelFormats.Gray8, null, output);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Wm2Format.Write not implemented");
        }
    }
}
