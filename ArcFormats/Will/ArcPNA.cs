//! \file       ArcPNA.cs
//! \date       Wed Feb 17 23:02:21 2016
//! \brief      Pulltop multi-frame image.
//
// Copyright (C) 2016 by morkt
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

namespace GameRes.Formats.Will
{
    internal class PnaEntry : ImageEntry
    {
        public ImageMetaData    Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class PnaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PNA"; } }
        public override string Description { get { return "Pulltop multi-frame image format"; } }
        public override uint     Signature { get { return 0x50414E50; } } // 'PNAP'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 0x14;
            uint current_offset = index_offset + (uint)count*0x28;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size = file.View.ReadUInt32 (index_offset+0x24);
                if (size > 0)
                {
                    var imginfo = new ImageMetaData {
                        OffsetX = file.View.ReadInt32 (index_offset+8),
                        OffsetY = file.View.ReadInt32 (index_offset+0xC),
                        Width   = file.View.ReadUInt32 (index_offset+0x10),
                        Height  = file.View.ReadUInt32 (index_offset+0x14),
                        BPP     = 32,
                    };
                    var entry = new PnaEntry {
                        Name    = string.Format ("{0}#{1:D3}.tga", base_name, i),
                        Size    = size,
                        Offset  = current_offset,
                        Info    = imginfo,
                    };
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    current_offset += entry.Size;
                }
                index_offset += 0x28;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PnaEntry;
            if (null == pent)
                return base.OpenEntry (arc, entry);
            ImageData image;
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
                image = ImageFormat.Read (input);
            var bitmap = image.Bitmap;
            if (bitmap.Format.BitsPerPixel != 32)
            {
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
            }
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels (pixels, stride, 0);

            // restore colors premultiplied by alpha
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i+3];
                if (alpha != 0 && alpha != 0xFF)
                {
                    pixels[i]   = (byte)(pixels[i]   * 0xFF / alpha);
                    pixels[i+1] = (byte)(pixels[i+1] * 0xFF / alpha);
                    pixels[i+2] = (byte)(pixels[i+2] * 0xFF / alpha);
                }
            }
            return TgaStream.Create (pent.Info, pixels);
        }
    }
}
