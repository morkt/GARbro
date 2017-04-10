//! \file       ArcG00.cs
//! \date       Wed May 04 13:21:19 2016
//! \brief      RealLive engine multi-frame image.
//
// Copyright (C) 2016-2017 by morkt
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
using System.Windows.Media;

namespace GameRes.Formats.RealLive
{
    internal class G00Entry : PackedEntry
    {
        public override string Type { get { return "image"; } }
        public int  X;
        public int  Y;
    }

    internal class G00Archive : ArcFile
    {
        public ImageMetaData    ImageInfo;
        public byte[]           Bitmap;

        public G00Archive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData info, byte[] bitmap)
            : base (arc, impl, dir)
        {
            ImageInfo = info;
            Bitmap = bitmap;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class G00Opener : ArchiveFormat
    {
        public override string         Tag { get { return "G00/v2"; } }
        public override string Description { get { return "RealLive engine multi-frame image"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public G00Opener ()
        {
            Extensions = new string[] { "g00" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".g00"))
                return null;
            if (file.View.ReadByte (0) != 2)
                return null;
            uint width  = file.View.ReadUInt16 (1);
            uint height = file.View.ReadUInt16 (3);
            if (0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            int count = file.View.ReadInt16 (5);
            if (count <= 1 || count > 0x100)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);

            uint index_offset = 9;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new G00Entry {
                    Name = string.Format ("{0}#{1:D3}", base_name, i),
                    X = file.View.ReadInt32 (index_offset),
                    Y = file.View.ReadInt32 (index_offset+4),
                };
                dir.Add (entry);
                index_offset += 0x18;
            }
            byte[] bitmap;
            using (var input = file.CreateStream (index_offset))
                bitmap = G00Reader.LzDecompress (input, 2, 1);

            using (var input = new BinMemoryStream (bitmap))
            {
                if (input.ReadInt32() != count)
                    return null;
                for (int i = 0; i < count; ++i)
                {
                    dir[i].Offset = input.ReadUInt32();
                    dir[i].Size   = input.ReadUInt32();
                }
            }
            dir = dir.Where (e => e.Size != 0).ToList();
            if (0 == dir.Count)
                return null;
            var info = new ImageMetaData { Width = width, Height = height, BPP = 32 };
            return new G00Archive (file, this, dir, info, bitmap);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var g00arc = (G00Archive)arc;
            var g00ent = (G00Entry)entry;
            var input = new BinMemoryStream (g00arc.Bitmap);
            return new G00TileDecoder (input, g00arc.ImageInfo, g00ent);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var g00arc = (G00Archive)arc;
            return new BinMemoryStream (g00arc.Bitmap, (int)entry.Offset, (int)entry.Size);
        }
    }

    internal sealed class G00TileDecoder : BinaryImageDecoder
    {
        private G00Entry g00ent;

        public G00TileDecoder (IBinaryStream input, ImageMetaData info, G00Entry entry)
            : base (input, info)
        {
            g00ent = entry;
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = g00ent.Offset;
            int tile_type = m_input.ReadUInt16();
            int count = m_input.ReadUInt16();
            if (tile_type != 1)
                throw new InvalidFormatException();
            m_input.Seek (0x70, SeekOrigin.Current);
            int dst_stride = (int)Info.Width * 4;
            var pixels = new byte[(int)Info.Height * dst_stride];
            for (int i = 0; i < count; ++i)
            {
                int tile_x = m_input.ReadUInt16();
                int tile_y = m_input.ReadUInt16();
                m_input.ReadInt16();
                int tile_width = m_input.ReadUInt16();
                int tile_height = m_input.ReadUInt16();
                m_input.Seek (0x52, SeekOrigin.Current);

                tile_x += g00ent.X;
                tile_y += g00ent.Y;
                if (tile_x + tile_width  > Info.Width ||
                    tile_y + tile_height > Info.Height)
                    throw new InvalidFormatException();
                int dst = tile_y * dst_stride + tile_x * 4;
                int tile_stride = tile_width * 4;
                for (int row = 0; row < tile_height; ++row)
                {
                    m_input.Read (pixels, dst, tile_stride);
                    dst += dst_stride;
                }
            }
            return ImageData.Create (Info, PixelFormats.Bgra32, null, pixels, dst_stride);
        }
    }
}
