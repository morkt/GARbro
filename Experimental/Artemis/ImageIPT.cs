//! \file       ImageIPT.cs
//! \date       2019 Jan 27
//! \brief      IPT composite image desciption.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Artemis
{
    internal class IptTile
    {
        public int      Id;
        public string   FileName;
        public int      X;
        public int      Y;
    }

    internal class IptMetaData : ImageMetaData
    {
        public string   Mode;
        public string   BaseName;
        public IEnumerable<IptTile> Tiles;
    }

    [Export(typeof(ImageFormat))]
    public class IptFormat : ImageFormat
    {
        public override string         Tag { get { return "IPT"; } }
        public override string Description { get { return "Artemis composite image descriptor"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".ipt"))
                return null;

            var parser = new IPTParser();
            parser.Parse (file.AsStream);
            var ipt = parser.RootObject["ipt"] as IPTObject;
            if (null == ipt)
                return null;
            var mode = ipt["mode"] as string;
            var canvas = ipt["base"] as IPTObject;
            if (null == mode || null == canvas)
                return null;
            var tiles = ipt.Values.Cast<IPTObject>() .Select (t => new IptTile {
                Id = (int)t["id"],
                FileName = (string)t["file"],
                X = (int)t["x"],
                Y = (int)t["y"],
            }); // XXX order by Id?
            if ("cut" == mode && !tiles.Any())
                return null;
            return new IptMetaData {
                Width = (uint)(int)canvas["w"],
                Height = (uint)(int)canvas["h"],
                OffsetX = (int)canvas["x"],
                OffsetY = (int)canvas["y"],
                BPP = 32,
                Mode = mode,
                BaseName = (string)canvas.Values[0],
                Tiles = tiles.ToList(),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (IptMetaData)info;
            PixelFormat format;
            if ("cut" == meta.Mode)
                format = PixelFormats.Bgra32;
            else if ("diff" == meta.Mode)
                format = PixelFormats.Bgr32;
            else
                throw new InvalidFormatException (string.Format ("Not supported IPT tile mode '{0}'.", meta.Mode));
            var canvas = new WriteableBitmap (meta.iWidth, meta.iHeight, ImageData.DefaultDpiX,
                                              ImageData.DefaultDpiY, format, null);
            var base_dir = VFS.GetDirectoryName (file.Name);
            try
            {
                canvas.Lock();
                if ("diff" == meta.Mode)
                {
                    var base_name = VFS.CombinePath (base_dir, meta.BaseName + ".png");
                    ReadIntoCanvas (base_name, canvas, 0, 0);
                }
                foreach (var tile in meta.Tiles)
                {
                    var tile_name = VFS.CombinePath (base_dir, tile.FileName + ".png");
                    ReadIntoCanvas (tile_name, canvas, tile.X, tile.Y, true);
                }
            }
            finally
            {
                canvas.Unlock();
            }
            canvas.Freeze();
            return new ImageData (canvas, meta);
        }

        void ReadIntoCanvas (string filename, WriteableBitmap output, int x, int y, bool blend = false)
        {
            if (y >= output.PixelHeight || x >= output.PixelWidth)
                return;
            var tile = ReadBitmapFromFile (filename);
            int src_x = x >= 0 ? 0 : -x;
            int src_y = y >= 0 ? 0 : -y;
            var width  = Math.Min (tile.PixelWidth - src_x,  output.PixelWidth  - x);
            var height = Math.Min (tile.PixelHeight - src_y, output.PixelHeight - y);
            var source = new Int32Rect (src_x, src_y, width, height);
            if (!source.HasArea)
                return;
            if (blend && tile.Format == PixelFormats.Bgra32)
            {
                BlendBitmap (tile, source, output, x, y);
            }
            else
            {
                int dst_stride = output.BackBufferStride;
                int offset = y * dst_stride + x * 4;
                var buf_pos = output.BackBuffer + offset;
                var size = output.PixelHeight * dst_stride - offset;
                tile.CopyPixels (source, (IntPtr)buf_pos, size, dst_stride);
            }
            var target = new Int32Rect (x, y, width, height);
            output.AddDirtyRect (target);
        }

        void BlendBitmap (BitmapSource bitmap, Int32Rect source, WriteableBitmap output, int x, int y)
        {
            int src_stride = source.Width * 4;
            var pixels = new byte[src_stride * source.Height];
            bitmap.CopyPixels (source, pixels, src_stride, 0);
            unsafe
            {
                int dst_stride = output.BackBufferStride;
                int offset = y * dst_stride + x * 4;
                byte* buffer = (byte*)(output.BackBuffer + offset);
                int src = 0;
                for (int h = 0; h < source.Height; ++h)
                {
                    int dst = 0;
                    for (int w = 0; w < source.Width; ++w)
                    {
                        byte src_alpha = pixels[src+3];
                        if (src_alpha > 0)
                        {
                            if (0xFF == src_alpha || 0 == buffer[dst+3])
                            {
                                buffer[dst  ] = pixels[src];
                                buffer[dst+1] = pixels[src+1];
                                buffer[dst+2] = pixels[src+2];
                            }
                            else
                            {
                                buffer[dst+0] = (byte)((pixels[src+0] * src_alpha + buffer[dst+0] * (0xFF - src_alpha)) / 0xFF);
                                buffer[dst+1] = (byte)((pixels[src+1] * src_alpha + buffer[dst+1] * (0xFF - src_alpha)) / 0xFF);
                                buffer[dst+2] = (byte)((pixels[src+2] * src_alpha + buffer[dst+2] * (0xFF - src_alpha)) / 0xFF);
                            }
                            buffer[dst+3] = src_alpha;
                        }
                        dst += 4;
                        src += 4;
                    }
                    buffer += dst_stride;
                }
            }
        }

        BitmapSource ReadBitmapFromFile (string filename)
        {
            using (var input = VFS.OpenBinaryStream (filename))
            {
                var image = Read (input);
                if (null == image)
                    throw new InvalidFormatException ("Invalid IPT tile format.");
                var bitmap = image.Bitmap;
                if (bitmap.Format.BitsPerPixel != 32)
                    bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);
                return bitmap;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("IptFormat.Write not implemented");
        }
    }
}
