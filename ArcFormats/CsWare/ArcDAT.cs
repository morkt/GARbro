//! \file       ArcDAT.cs
//! \date       2017 Dec 21
//! \brief      C's Ware BLITZ resource archive.
//
// Copyright (C) 2017 by morkt
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
using System.Windows.Media;
using GameRes.Compression;

namespace GameRes.Formats.CsWare
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CSWARE"; } }
        public override string Description { get { return "C's Ware BLITZ resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint packed_size = file.View.ReadUInt32 (4);
            if (packed_size >= file.MaxOffset)
                return null;
            if (file.View.ReadByte (8) != 0x78) // zlib stream signature
                return null;
            long data_offset = 8 + packed_size;
            using (var input = file.CreateStream (8, packed_size))
            using (var unpacked = new ZLibStream (input, CompressionMode.Decompress))
            using (var index = new BinaryStream (unpacked, file.Name))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x18);
                    if (string.IsNullOrEmpty (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.ReadUInt32() + data_offset;
                    entry.Size   = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x36 || !arc.File.View.AsciiEqual (entry.Offset, "BM"))
                return base.OpenImage (arc, entry);
            uint header_size = arc.File.View.ReadUInt32 (entry.Offset + 0xA);
            if (header_size < 0x36 || header_size > entry.Size)
                return base.OpenImage (arc, entry);
            int height = arc.File.View.ReadInt32 (entry.Offset + 0x16);
            var info = new BmMetaData {
                Width = arc.File.View.ReadUInt32 (entry.Offset + 0x12),
                Height = (uint)Math.Abs (height),
                BPP = arc.File.View.ReadUInt16 (entry.Offset + 0x1C),
                DataOffset = header_size,
                IsFlipped = height > 0,
                IsCompressed = arc.File.View.ReadUInt32 (0x32) != 0,
            };
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new BmpDecoder (input, info);
        }
    }

    internal class BmMetaData : ImageMetaData
    {
        public uint DataOffset;
        public bool IsFlipped;
        public bool IsCompressed;
    }

    internal class BmpDecoder : BinaryImageDecoder
    {
        public BmpDecoder (IBinaryStream input, BmMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            var meta = (BmMetaData)Info;
            m_input.Position = meta.DataOffset;
            int stride = (int)Info.Width * Info.BPP / 8;
            var pixels = new byte[stride * (int)Info.Height];
            if (meta.IsCompressed)
            {
                using (var unpacked = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
                    unpacked.Read (pixels, 0, pixels.Length);
            }
            else
            {
                m_input.Read (pixels, 0, pixels.Length);
            }
            PixelFormat format;
            if (24 == Info.BPP)
                format = PixelFormats.Bgr24;
            else if (32 == Info.BPP)
                format = PixelFormats.Bgra32;
            else
                throw new InvalidFormatException();
            if (meta.IsFlipped)
                return ImageData.CreateFlipped (Info, format, null, pixels, stride);
            else
                return ImageData.Create (Info, format, null, pixels, stride);
        }
    }
}
