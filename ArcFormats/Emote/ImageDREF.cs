//! \file       ImageDREF.cs
//! \date       2018 Jan 22
//! \brief      PSB-referenced compound image.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// not exactly related to Emote engine, but defined in Emote namespace for convenience

namespace GameRes.Formats.Emote
{
    internal class DrefMetaData : ImageMetaData
    {
        public IEnumerable<Tuple<string, string>> Layers;
    }

    [Export(typeof(ImageFormat))]
    public class DrefFormat : ImageFormat
    {
        public override string         Tag { get { return "DREF"; } }
        public override string Description { get { return "DPAK-referenced compound image"; } }
        public override uint     Signature { get { return 0x0070FEFF; } }

        public DrefFormat ()
        {
            // 'psb:' string with possible byte-order-mark prepended
            Signatures = new uint[] { 0x0070FEFF, 0x70BFBBEF, 0x3A627370, 0x00730070 };
        }

        static readonly Regex PathRe = new Regex (@"^psb://([^/]+)/(.+)");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".dref"))
                return null;
            var dir = VFS.GetDirectoryName (file.Name);
            using (var input = new StreamReader (file.AsStream, Encoding.Unicode, true, 1024, true))
            {
                var layers = new List<Tuple<string, string>>();
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    var match = PathRe.Match (line);
                    if (!match.Success)
                        return null;
                    var pak_name = match.Groups[1].Value;
                    if (!VFS.FileExists (pak_name))
                        return null;
                    pak_name = VFS.CombinePath (dir, pak_name);
                    layers.Add (Tuple.Create (pak_name, match.Groups[2].Value));
                }
                if (0 == layers.Count)
                    return null;
                return new DrefMetaData { Layers = layers, BPP = 32 };
            }
        }

        static readonly ResourceInstance<ArchiveFormat> Psb = new ResourceInstance<ArchiveFormat> ("PSB/EMOTE");

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (DrefMetaData)info;
            ArcFile dpak = null;
            try
            {
                int layers_count = meta.Layers.Count();
                WriteableBitmap canvas = null;
                foreach (var path in meta.Layers)
                {
                    if (null == dpak || dpak.File.Name != path.Item1)
                    {
                        if (dpak != null)
                        {
                            dpak.Dispose();
                            dpak = null;
                        }
                        var view = VFS.OpenView (path.Item1);
                        try
                        {
                            dpak = Psb.Value.TryOpen (view);
                            if (null == dpak)
                                throw new InvalidFormatException ();
                        }
                        catch
                        {
                            view.Dispose();
                            throw;
                        }
                    }
                    var entry = dpak.Dir.FirstOrDefault (e => e.Name == path.Item2);
                    if (null == entry)
                        throw new InvalidFormatException();
                    using (var decoder = dpak.OpenImage (entry))
                    {
                        if (1 == layers_count)
                            return decoder.Image;
                        if (null == canvas)
                        {
                            canvas = new WriteableBitmap (decoder.Image.Bitmap);
                            meta.Width = decoder.Info.Width;
                            meta.Height = decoder.Info.Height;
                            meta.OffsetX = decoder.Info.OffsetX;
                            meta.OffsetY = decoder.Info.OffsetY;
                        }
                        else
                        {
                            BlendLayer (canvas, decoder.Image);
                        }
                    }
                }
                if (null == canvas)
                    throw new InvalidFormatException();
                canvas.Freeze();
                return new ImageData (canvas, meta);
            }
            finally
            {
                if (dpak != null)
                    dpak.Dispose();
            }
        }

        void BlendLayer (WriteableBitmap canvas, ImageData layer)
        {
            BitmapSource source = layer.Bitmap;
            if (source.Format.BitsPerPixel != 32)
                source = new FormatConvertedBitmap (source, PixelFormats.Bgra32, null, 0);

            // determine coordinates of the intersection of layer and canvas
            var src_rect = new Int32Rect (0, 0, source.PixelWidth, source.PixelHeight);
            if (layer.OffsetX < 0)
            {
                src_rect.X = -layer.OffsetX;
                src_rect.Width += layer.OffsetX;
            }
            if (layer.OffsetY < 0)
            {
                src_rect.Y = -layer.OffsetY;
                src_rect.Height += layer.OffsetY;
            }
            if (!src_rect.HasArea)
                return;
            var layer_rect = new Rectangle (layer.OffsetX, layer.OffsetY, source.PixelWidth, source.PixelHeight);
            var canvas_rect = new Rectangle (0, 0, canvas.PixelWidth, canvas.PixelHeight);
            layer_rect.Intersect (canvas_rect);
            if (layer_rect.Width <= 0 || layer_rect.Height <= 0)
                return;

            // copy out layer area
            int src_stride = src_rect.Width * 4;
            var pixels = new byte[src_stride * src_rect.Height];
            source.CopyPixels (src_rect, pixels, src_stride, 0);

            // perform blending within established coordinates
            int pixel_size = (canvas.Format.BitsPerPixel + 7) / 8;
            int canvas_stride = canvas.BackBufferStride;
            int dst_row = layer_rect.Y * canvas_stride + layer_rect.X * pixel_size;
            canvas.Lock();
            unsafe
            {
                byte* buffer = (byte*)canvas.BackBuffer;
                for (int src = 0; src < pixels.Length; src += src_stride)
                {
                    byte* dst = buffer+dst_row;
                    for (int x = 0; x < src_stride; x += 4)
                    {
                        byte src_alpha = pixels[src+x+3];
                        if (0xFF == src_alpha)
                        {
                            for (int i = 0; i < pixel_size; ++i)
                                dst[i] = pixels[src+x+i];
                        }
                        else if (src_alpha > 0)
                        {
                            dst[0] = (byte)((pixels[src+x+0] * src_alpha + dst[0] * (0xFF - src_alpha)) / 0xFF);
                            dst[1] = (byte)((pixels[src+x+1] * src_alpha + dst[1] * (0xFF - src_alpha)) / 0xFF);
                            dst[2] = (byte)((pixels[src+x+2] * src_alpha + dst[2] * (0xFF - src_alpha)) / 0xFF);
                            if (pixel_size > 3)
                                dst[3] = (byte)Math.Max (src_alpha, dst[3]);
                        }
                        dst += pixel_size;
                    }
                    dst_row += canvas_stride;
                }
            }
            canvas.Unlock();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DrefFormat.Write not implemented");
        }
    }
}
