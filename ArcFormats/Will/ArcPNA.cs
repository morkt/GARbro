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
                        Name    = string.Format ("{0}#{1:D3}", base_name, i),
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

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var pent = (PnaEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PnaDecoder (input, pent.Info);
        }
    }

    internal sealed class PnaDecoder : IImageDecoder
    {
        IBinaryStream       m_input;
        ImageMetaData       m_info;
        ImageData           m_image;

        public Stream            Source { get { m_input.Position = 0; return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    var pixels = ReadPixels();
                    m_image = ImageData.Create (m_info, PixelFormats.Bgra32, null, pixels);
                }
                return m_image;
            }
        }

        public PnaDecoder (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
        }

        byte[] ReadPixels ()
        {
            var image = ImageFormat.Read (m_input);
            if (null == image)
                throw new InvalidFormatException();
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
            return pixels;
        }

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
