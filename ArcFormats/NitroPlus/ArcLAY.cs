//! \file       ArcLAY.cs
//! \date       2018 Oct 18
//! \brief      MAGES engine composite layers.
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
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.NitroPlus
{
    internal class LayEntry : Entry
    {
        public uint Id;
        public int  First;
        public int  Count;
    }

    internal class LayCoord
    {
        public float TargetX, TargetY;
        public float SourceX, SourceY;
    }

    internal class LayArchive : ArcFile
    {
        public readonly BitmapSource        Source;
        public readonly IList<LayCoord>     Tiles;
        public readonly IDictionary<uint, LayEntry> LayerMap;

        public LayArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, BitmapSource image, IList<LayCoord> tiles)
            : base (arc, impl, dir)
        {
            Source = image;
            Tiles = tiles;
            LayerMap = new Dictionary<uint, LayEntry> (dir.Count);
            foreach (LayEntry entry in dir)
            {
                LayerMap[entry.Id] = entry;
            }
        }

        public IEnumerable<LayCoord> GetTiles (LayEntry layer)
        {
            return Tiles.Skip (layer.First).Take (layer.Count);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class LayOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LAY/MAGES"; } }
        public override string Description { get { return "MAGES engine composite image archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const int DefaultWidth  = 1920;
        const int DefaultHeight = 1080;
        const int BlockSize     = 32;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".lay"))
                return null;
            int tile_count = file.View.ReadInt32 (0);
            int coord_count = file.View.ReadInt32 (4);
            if (!IsSaneCount (tile_count) || !IsSaneCount (coord_count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name).TrimEnd ('_');
            var png_name = VFS.ChangeFileName (file.Name, base_name + ".png");
            if (!VFS.FileExists (png_name))
                return null;
            ImageData image;
            var png_entry = VFS.FindFile (png_name);
            using (var decoder = VFS.OpenImage (png_entry))
                image = decoder.Image;
            using (var input = file.CreateStream())
            using (var index = new BinaryReader (input))
            {
                input.Position = 8;
                var dir = new List<Entry> (tile_count);
                for (int i = 0; i < tile_count; ++i)
                {
                    uint id = index.ReadUInt32();
                    int first = index.ReadInt32();
                    int count = index.ReadInt32();
                    var name = string.Format ("{0}#{1:X8}", base_name, id);
                    var entry = new LayEntry {
                        Name = name, Type = "image", Offset = 0,
                        Id = id, First = first, Count = count
                    };
                    dir.Add (entry);
                }
                var tiles = new List<LayCoord> (coord_count);
                for (int i = 0; i < coord_count; ++i)
                {
                    var tile = new LayCoord();
                    tile.TargetX = index.ReadSingle() + 1;
                    tile.TargetY = index.ReadSingle() + 1;
                    tile.SourceX = index.ReadSingle() - 1;
                    tile.SourceY = index.ReadSingle() - 1;
                    tiles.Add (tile);
                }
                return new LayArchive (file, this, dir, image.Bitmap, tiles);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var lent = (LayEntry)entry;
            var larc = (LayArchive)arc;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                if (lent.Id != 1)
                {
                    LayEntry base_layer;
                    if (larc.LayerMap.TryGetValue (1, out base_layer))
                        DrawLayer (context, larc.Source, larc.GetTiles (base_layer));
                }
                if ((lent.Id >> 28) == 4)
                {
                    uint face_id = (lent.Id >> 8) & 0xF | 0x20000000;
                    LayEntry face_layer;
                    if (larc.LayerMap.TryGetValue (face_id, out face_layer) ||
                        larc.LayerMap.TryGetValue (face_id-1, out face_layer))
                        DrawLayer (context, larc.Source, larc.GetTiles (face_layer));
                }
                DrawLayer (context, larc.Source, larc.GetTiles (lent));
            }
            var bmp = new RenderTargetBitmap (DefaultWidth, DefaultHeight, ImageData.DefaultDpiX,
                                              ImageData.DefaultDpiY, PixelFormats.Pbgra32);
            bmp.Render (visual);
            return new BitmapSourceDecoder (bmp);
        }

        void DrawLayer (DrawingContext context, BitmapSource source, IEnumerable<LayCoord> tiles)
        {
            foreach (var coord in tiles)
            {
                var src_rect = new Int32Rect ((int)coord.SourceX, (int)coord.SourceY, BlockSize, BlockSize);
                var tile = new CroppedBitmap (source, src_rect);
                var dst_rect = new Rect (coord.TargetX + DefaultWidth / 2, coord.TargetY + DefaultHeight / 2,
                                         BlockSize, BlockSize);
                context.DrawImage (tile, dst_rect);
            }
        }
    }
}
