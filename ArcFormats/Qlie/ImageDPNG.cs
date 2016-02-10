//! \file       ImageDPNG.cs
//! \date       Fri Nov 06 14:16:24 2015
//! \brief      QLIE tiled PNG image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Qlie
{
    internal class DpngMetaData : ImageMetaData
    {
        public int TileCount;
    }

    [Export(typeof(ImageFormat))]
    public class DpngFormat : ImageFormat
    {
        public override string         Tag { get { return "DPNG"; } }
        public override string Description { get { return "QLIE tiled image format"; } }
        public override uint     Signature { get { return 0x474E5044; } } // 'DPNG'

        public DpngFormat ()
        {
            Extensions = new string[] { "png" };
        }

        public override ImageMetaData ReadMetaData (Stream stream)
        {
            stream.Position = 8;
            using (var header = new ArcView.Reader (stream))
            {
                var info = new DpngMetaData { BPP = 32 };
                info.TileCount = header.ReadInt32();
                if (info.TileCount <= 0)
                    return null;
                info.Width     = header.ReadUInt32();
                info.Height    = header.ReadUInt32();
                return info;
            }
        }

        public override ImageData Read (Stream stream, ImageMetaData info)
        {
            var meta = (DpngMetaData)info;
            var bitmap = new WriteableBitmap ((int)info.Width, (int)info.Height,
                ImageData.DefaultDpiX, ImageData.DefaultDpiY, PixelFormats.Pbgra32, null);
            long next_tile = 0x14;
            using (var dpng = new ArcView.Reader (stream))
            {
                for (int i = 0; i < meta.TileCount; ++i)
                {
                    stream.Position = next_tile;
                    int x = dpng.ReadInt32();
                    int y = dpng.ReadInt32();
                    int width = dpng.ReadInt32();
                    int height = dpng.ReadInt32();
                    uint size = dpng.ReadUInt32();
                    stream.Seek (8, SeekOrigin.Current);
                    next_tile = stream.Position + size;
                    if (0 == size)
                        continue;
                    using (var png = new StreamRegion (stream, stream.Position, size, true))
                    {
                        var decoder = new PngBitmapDecoder (png,
                            BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        var frame = new FormatConvertedBitmap (decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                        int stride = frame.PixelWidth * 4;
                        var pixels = new byte[stride * frame.PixelHeight];
                        frame.CopyPixels (pixels, stride, 0);
                        var rect = new Int32Rect (0, 0, frame.PixelWidth, frame.PixelHeight);
                        bitmap.WritePixels (rect, pixels, stride, x, y);
                    }
                }
            }
            bitmap.Freeze();
            return new ImageData (bitmap, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DpngFormat.Write not implemented");
        }
    }
}
