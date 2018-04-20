//! \file       ImageDZI.cs
//! \date       Sat Jul 11 07:13:28 2015
//! \brief      Malie DZI tiled images set.
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
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Malie
{
    internal class DziTile
    {
        public int      X, Y;
        public string   FileName;
    }

    internal class DziMetaData : ImageMetaData
    {
        public IEnumerable<IEnumerable<DziTile>> Tiles;
    }

    [Export(typeof(ImageFormat))]
    public class DziFormat : ImageFormat
    {
        public override string         Tag { get { return "DZI"; } }
        public override string Description { get { return "Malie tiled image descriptor"; } }
        public override uint     Signature { get { return 0x0D495A44; } } // 'DZI'

        static readonly Regex pair_re = new Regex (@"^(\d+),(\d+)");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var dir = VFS.GetDirectoryName (file.Name);
            var tex = VFS.FindFile (VFS.CombinePath (dir, "tex"));
            if (!(tex is SubDirEntry))
                return null;
            using (var reader = new StreamReader (file.AsStream, Encoding.UTF8, false, 2048, true))
            {
                reader.ReadLine(); // skip signature
                string line = reader.ReadLine();
                if (null == line)
                    return null;
                var match = pair_re.Match (line);
                if (!match.Success)
                    return null;
                uint width  = Convert.ToUInt16 (match.Groups[1].Value);
                uint height = Convert.ToUInt16 (match.Groups[2].Value);
                line = reader.ReadLine();
                if (null == line)
                    return null;
                int count = Convert.ToInt16 (line);
                if (0 == count)
                    return null;
                var list = new List<IEnumerable<DziTile>> (count);
                for (int i = 0; i < count; ++i)
                {
                    line = reader.ReadLine();
                    if (null == line)
                        return null;
                    match = pair_re.Match (line);
                    if (!match.Success)
                        return null;
                    int block_w = Convert.ToUInt16 (match.Groups[1].Value);
                    int block_h = Convert.ToUInt16 (match.Groups[2].Value);
                    var tiles = new List<DziTile> (block_w * block_h);
                    int y = 0;
                    for (int j = 0; j < block_h; ++j)
                    {
                        int x = 0;
                        line = reader.ReadLine();
                        if (null == line)
                            return null;
                        line = line.TrimEnd();
                        foreach (var filename in line.Split (','))
                        {
                            if (!string.IsNullOrEmpty (filename))
                            {
                                var fullname = VFS.CombinePath (tex.Name, filename);
                                tiles.Add (new DziTile { X = x, Y = y, FileName = fullname });
                            }
                            x += 256;
                        }
                        y += 256;
                    }
                    list.Add (tiles);
                }
                return new DziMetaData
                {
                    Width = width,
                    Height = height,
                    BPP = 32,
                    Tiles = list,
                };
            }
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (DziMetaData)info;
            PixelFormat format = PixelFormats.Bgra32;
            var bitmap = new WriteableBitmap ((int)meta.Width, (int)meta.Height, ImageData.DefaultDpiX,
                                              ImageData.DefaultDpiY, format, null);
            int actual_width = 0;
            int actual_height = 0;
            byte[] pixels = null;
            foreach (var tile in meta.Tiles.First())
            {
                var image_entry = VFS.GetFiles (tile.FileName + ".*").FirstOrDefault();
                if (null == image_entry)
                    throw new FileNotFoundException (string.Format ("Tile '{0}' not found", tile.FileName));
                using (var input = VFS.OpenBinaryStream (image_entry))
                {
                    var image = Read (input);
                    if (null == image)
                        throw new FileFormatException ("Unknown DZI tile format");
                    var converted = image.Bitmap;
                    if (converted.Format != format)
                        converted = new FormatConvertedBitmap (converted, format, null, 0);
                    int stride = converted.PixelWidth * 4;
                    int tile_size = stride * converted.PixelHeight;
                    if (null == pixels || pixels.Length < tile_size)
                        pixels = new byte[tile_size];
                    converted.CopyPixels (pixels, stride, 0);
                    var width  = Math.Min (converted.PixelWidth,  bitmap.PixelWidth  - tile.X);
                    var height = Math.Min (converted.PixelHeight, bitmap.PixelHeight - tile.Y);
                    var rect = new Int32Rect (tile.X, tile.Y, width, height);
                    bitmap.WritePixels (rect, pixels, stride, 0);
                    if (tile.X + width > actual_width)
                        actual_width = tile.X + width;
                    if (tile.Y + height > actual_height)
                        actual_height = tile.Y + height;
                }
            }
            BitmapSource result = bitmap;
            if (actual_width < bitmap.PixelWidth || actual_height < bitmap.PixelHeight)
            {
                var rect = new Int32Rect (0, 0, actual_width, actual_height);
                result = new CroppedBitmap (bitmap, rect);
            }
            result.Freeze();
            return new ImageData (result, meta);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DziFormat.Write not implemented");
        }
    }
}
