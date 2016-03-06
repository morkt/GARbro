//! \file       ImageDIF.cs
//! \date       Sun Mar 06 14:36:12 2016
//! \brief      M no Violet engine diff images.
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

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.MnoViolet
{
    internal class DifMetaData : ImageMetaData
    {
        public Entry            BaseEntry;
        public ImageFormat      BaseFormat;
        public ImageMetaData    BaseInfo;
        public int              PackedIndexSize;
        public int              IndexSize;
        public int              PackedDiffSize;
        public int              DiffDataSize;
        public int              DiffCount;
    }

    /// <summary>
    /// 'dif' files contain reference to an image they're based on and compressed difference data.
    /// filename is specified without extension and base file usually resides within some other archive.
    /// this implementation looks for the first file matching 'FILENAME.*' pattern within the same directory
    /// as 'dif' itself and takes it as a base image.
    /// so, both images and diff files should be placed into the same directory beforehand.
    /// </summary>

    [Export(typeof(ImageFormat))]
    public class DifFormat : ImageFormat
    {
        public override string         Tag { get { return "DIF/MnV"; } }
        public override string Description { get { return "M no Violet incremental image format"; } }
        public override uint     Signature { get { return 0x00666964; } } // 'dif'

        public DifFormat ()
        {
            Extensions = new string[] { "dif" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            var header = new byte[0x7C];
            if (header.Length != stream.Read (header, 0, header.Length))
                return null;
            var base_name = Binary.GetCString (header, 4, 100);
            if (string.IsNullOrEmpty (base_name))
                return null;
            var files = VFS.GetFiles (base_name+".*");
            if (!files.Any())
                return null;
            var base_entry = files.First();
            using (var input = VFS.OpenSeekableStream (base_entry))
            {
                // ReadMetaData isn't supplied with a filename being processed, so infinite recursion can't be
                // prevented here unless we save state in a static member.
                var format = ImageFormat.FindFormat (input, base_entry.Name);
                if (null == format)
                    return null;
                format.Item2.FileName = base_entry.Name;
                return new DifMetaData
                {
                    Width = format.Item2.Width,
                    Height = format.Item2.Height,
                    BPP = 24,
                    BaseEntry = base_entry,
                    BaseFormat = format.Item1,
                    BaseInfo = format.Item2,
                    PackedIndexSize = LittleEndian.ToInt32 (header, 0x68),
                    IndexSize = LittleEndian.ToInt32 (header, 0x6C),
                    PackedDiffSize = LittleEndian.ToInt32 (header, 0x70),
                    DiffDataSize = LittleEndian.ToInt32 (header, 0x74),
                    DiffCount = LittleEndian.ToInt32 (header, 0x78),
                };
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (DifMetaData)info;
            BitmapSource base_bitmap;
            using (var input = VFS.OpenSeekableStream (meta.BaseEntry))
            {
                var image = meta.BaseFormat.Read (input, meta.BaseInfo);
                base_bitmap = image.Bitmap;
            }
            stream.Position = 0x7C;
            var index = new byte[meta.IndexSize];
            using (var input = new LzssStream (stream, LzssMode.Decompress, true))
                if (index.Length != input.Read (index, 0, index.Length))
                    throw new EndOfStreamException();

            if (base_bitmap.Format.BitsPerPixel != 24)
                base_bitmap = new FormatConvertedBitmap (base_bitmap, PixelFormats.Bgr24, null, 0);

            int src_stride = base_bitmap.PixelWidth * 3;
            int dst_stride = (src_stride + 3) & ~3;
            var pixels = new byte[dst_stride * base_bitmap.PixelHeight];
            int row_offset = 0;
            var rect = new Int32Rect (0, base_bitmap.PixelHeight, base_bitmap.PixelWidth, 1);
            for (rect.Y = base_bitmap.PixelHeight-1; rect.Y >= 0; --rect.Y)
            {
                base_bitmap.CopyPixels (rect, pixels, src_stride, row_offset);
                row_offset += dst_stride;
            }

            stream.Position = 0x7C + meta.PackedIndexSize;
            using (var diff = new LzssStream (stream, LzssMode.Decompress, true))
            {
                int index_src = 0;
                for (int i = 0; i < meta.DiffCount; ++i)
                {
                    int offset = LittleEndian.ToInt32 (index, index_src);
                    int size   = LittleEndian.ToInt32 (index, index_src+4);
                    index_src += 8;
                    diff.Read (pixels, offset, size);
                }
                return ImageData.CreateFlipped (info, PixelFormats.Bgr24, null, pixels, dst_stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("DifFormat.Write not implemented");
        }
    }
}
