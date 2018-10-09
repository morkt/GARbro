//! \file       ArcCHR.cs
//! \date       Mon Oct 26 07:26:39 2015
//! \brief      Multi-frame PNG archive.
//
// Copyright (C) 2015-2018 by morkt
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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Palette
{
    internal class CharEntry : Entry
    {
        public int  OffsetX;
        public int  OffsetY;
    }

    internal class VirtualCharEntry : Entry
    {
        public CharEntry    Source;
    }

    [Export(typeof(ArchiveFormat))]
    public class ChrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CHR/Palette"; } }
        public override string Description { get { return "Palette multi-frame PNG archive"; } }
        public override uint     Signature { get { return 0x72616863; } } // 'char'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }
        
        public ChrOpener ()
        {
            Extensions = new string[] { "chr" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (4);
            int count = file.View.ReadInt32 (index_offset)+1;
            if (!IsSaneCount (count))
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);

            var dir = new List<Entry> (count);
            dir.Add (new Entry {
                Name = string.Format ("{0}#0.png", base_name),
                Type = "image", Offset = 8, Size = index_offset-8
            });
            index_offset += 4;
            for (int i = 1; i < count; ++i)
            {
                uint name_length = file.View.ReadByte (index_offset++);
                var name = file.View.ReadString (index_offset, name_length);
                uint size = file.View.ReadUInt32 (index_offset+name_length);
                index_offset += name_length + 4;
                if (size > 8)
                {
                    var entry = new CharEntry
                    {
                        Name = string.Format ("{0}#{1}.png", base_name, name),
                        Type = "image",
                        Offset = index_offset+8,
                        Size = size-8,
                        OffsetX = file.View.ReadInt16 (index_offset),
                        OffsetY = file.View.ReadInt16 (index_offset+2),
                        //Width  = file.View.ReadUInt16 (index_offset+4),
                        //Height = file.View.ReadUInt16 (index_offset+6),
                    };
                    dir.Add (entry);
                    var virt_entry = new VirtualCharEntry {
                        Name = string.Format ("{0}#blend#{1}.png", base_name, name),
                        Type = "image",
                        Source = entry,
                        Offset = 0,
                    };
                    dir.Add (virt_entry);
                }
                index_offset += size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (entry is VirtualCharEntry)
                return BlendEntry (arc, entry as VirtualCharEntry);
            var input = arc.OpenBinaryEntry (entry);
            var info = ImageFormat.Png.ReadMetaData (input);
            input.Position = 0;
            return new ImageFormatDecoder (input, ImageFormat.Png, info);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry is VirtualCharEntry)
                return Stream.Null;
            int extra_length = PgaFormat.PngHeader.Length + PgaFormat.PngFooter.Length;
            var png = new MemoryStream ((int)entry.Size + extra_length + 17);
            var cent = entry as CharEntry;
            png.Write (PgaFormat.PngHeader, 0, PgaFormat.PngHeader.Length);
            using (var body = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                if (cent != null && cent != arc.Dir.First()
                    && (cent.OffsetX != 0 || cent.OffsetY != 0))
                {
                    // inject oFFs chunk into PNG stream
                    using (var reader = new BinaryReader (body, Encoding.ASCII, true))
                    using (var writer = new BinaryWriter (png, Encoding.ASCII, true))
                    {
                        var ihdr_size = reader.ReadInt32();
                        writer.Write (ihdr_size);
                        ihdr_size = Binary.BigEndian (ihdr_size);
                        writer.Write (reader.ReadBytes (ihdr_size+8));

                        writer.Write (Binary.BigEndian ((int)9));
                        int position = (int)writer.BaseStream.Position;
                        char[] tag = { 'o', 'F', 'F', 's' };
                        writer.Write (tag);
                        writer.Write (Binary.BigEndian (cent.OffsetX));
                        writer.Write (Binary.BigEndian (cent.OffsetY));
                        writer.Write ((byte)0);
                        uint crc = Crc32.Compute (png.GetBuffer(), position, 13);
                        writer.Write (Binary.BigEndian (crc));
                    }
                }
                body.CopyTo (png);
            }
            png.Write (PgaFormat.PngFooter, 0, PgaFormat.PngFooter.Length);
            png.Position = 0;
            return png;
        }

        IImageDecoder BlendEntry (ArcFile arc, VirtualCharEntry entry)
        {
            using (var base_png = OpenEntry (arc, arc.Dir.First()))
            using (var overlay_png = OpenEntry (arc, entry.Source))
            {
                var decoder = new PngBitmapDecoder (base_png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var base_frame = decoder.Frames[0];
                decoder = new PngBitmapDecoder (overlay_png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var overlay_frame = decoder.Frames[0];
                int overlay_x = entry.Source.OffsetX;
                int overlay_y = entry.Source.OffsetY;
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawImage (base_frame, new Rect (0, 0, base_frame.PixelWidth, base_frame.PixelHeight));
                    context.DrawImage (overlay_frame, new Rect (overlay_x, overlay_y, overlay_frame.PixelWidth, overlay_frame.PixelHeight));
                }
                var bmp = new RenderTargetBitmap (base_frame.PixelWidth, base_frame.PixelHeight,
                                                  base_frame.DpiX, base_frame.DpiY, PixelFormats.Pbgra32);
                bmp.Render (visual);
                return new BitmapSourceDecoder (bmp);
            }
        }
    }
}
