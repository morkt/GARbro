//! \file       ImageLSG.cs
//! \date       2022 Apr 12
//! \brief      Kogado image format.
//
// Copyright (C) 2022 by morkt
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
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Kogado
{
    internal class LsgMetaData : ImageMetaData
    {
        public int BitmapSize;
    }

    [Export(typeof(ImageFormat))]
    public class LsgFormat : ImageFormat
    {
        public override string         Tag { get { return "LSG"; } }
        public override string Description { get { return "Kogado image format"; } }
        public override uint     Signature { get { return 0x4D42; } } // 'BM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            var info = new LsgMetaData {
                Width  = header.ToUInt32 (0x0C),
                Height = header.ToUInt32 (0x10),
                BPP    = header.ToInt32 (8),
                BitmapSize = header.ToInt32 (4),
            };
            if (info.BPP != 8 && info.BPP != 24)
                return null;
            return info;
        }

        static readonly string DefaultPaletteName = "base.pal";

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (LsgMetaData)info;
            file.Position = 0x14;
            var pixels = file.ReadBytes (meta.BitmapSize);
            PixelFormat format;
            BitmapPalette palette = null;
            if (meta.BPP == 8)
            {
                format = PixelFormats.Indexed8;
                palette = ReadDefaultPalette (file.Name);
                if (null == palette)
                    format = PixelFormats.Gray8;
            }
            else
            {
                format = PixelFormats.Bgr24;
            }
            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LsgFormat.Write not implemented");
        }

        internal BitmapPalette ReadDefaultPalette (string filename)
        {
            var pal_name = Path.ChangeExtension (filename, ".pal");
            if (!VFS.FileExists (pal_name))
                pal_name = VFS.ChangeFileName (filename, DefaultPaletteName);
            if (!VFS.FileExists (pal_name))
                return null;
            using (var input = VFS.OpenStream (pal_name))
            {
                return ReadPalette (input, 0x100, PaletteFormat.Rgb);
            }
        }
    }
}
