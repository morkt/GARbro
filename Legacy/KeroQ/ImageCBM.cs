//! \file       ImageCBM.cs
//! \date       2018 Aug 22
//! \brief      KeroQ bitmap format.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.KeroQ
{
    [Export(typeof(ImageFormat))]
    public class CbmFormat : ImageFormat
    {
        public override string         Tag { get { return "CBM"; } }
        public override string Description { get { return "KeroQ bitmap format"; } }
        public override uint     Signature { get { return 0x004D4243; } } // 'CBM'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x10);
            var length = header.ToUInt32 (0xC);
            if (file.Length - 0x10 != length)
                return null;
            return new ImageMetaData {
                Width  = header.ToUInt32 (4),
                Height = header.ToUInt32 (8),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            BitmapPalette palette = null;
            PixelFormat format = PixelFormats.Gray8;
            foreach (var pal_name in GetPaletteNames (info.FileName))
            {
                if (!VFS.FileExists (pal_name))
                    continue;
                try
                {
                    using (var pal = VFS.OpenStream (pal_name))
                    {
                        palette = ReadPalette (pal, 0x100, PaletteFormat.Bgr);
                        format = PixelFormats.Indexed8;
                    }
                }
                catch { /* ignore palette read errors */ }
                break;
            }
            file.Position = 0x10;
            var pixels = file.ReadBytes ((int)info.Width * (int)info.Height);
            return ImageData.Create (info, format, palette, pixels);
        }

        IEnumerable<string> GetPaletteNames (string filename)
        {
            var base_name = Path.GetFileNameWithoutExtension (filename);
            yield return VFS.ChangeFileName (filename, base_name + ".pal");
            if (base_name.Length > 3)
                base_name = base_name.Substring (0, 3);
            yield return VFS.ChangeFileName (filename, base_name + ".pal");
            yield return VFS.ChangeFileName (filename, base_name + "_2.pal");
            yield return VFS.ChangeFileName (filename, base_name + "_1.pal");
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CbmFormat.Write not implemented");
        }
    }
}
