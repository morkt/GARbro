//! \file       ArcPureMail.cs
//! \date       Sun Feb 12 04:18:56 2017
//! \brief      0verflow Game System resource archive.
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
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    internal class PmDatEntry : PackedEntry
    {
        public bool StoredSize;
    }

    [Export(typeof(ArchiveFormat))]
    public class PmDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PUREMAIL"; } }
        public override string Description { get { return "PureMail resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset <= 12 || file.MaxOffset > uint.MaxValue)
                return null;
            uint packed_size   = file.View.ReadUInt32 (file.MaxOffset-12) ^ 0xF0F0F0F0;
            uint unpacked_size = file.View.ReadUInt32 (file.MaxOffset-8)  ^ 0xF0F0F0F0;
            const uint entry_record_size = 0x50;
            int count = (int)(unpacked_size / entry_record_size);
            if (unpacked_size % entry_record_size != 0 || packed_size >= file.MaxOffset || !IsSaneCount (count))
                return null;

            var unpacked = new byte[unpacked_size];
            using (var packed = file.CreateStream (file.MaxOffset-12-packed_size, packed_size))
                LzUnpack (packed, unpacked);
            using (var index = new BinMemoryStream (unpacked))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    int flags = index.ReadInt32();
                    var name = index.ReadCString (0x40);
                    var entry = FormatCatalog.Instance.Create<PmDatEntry> (name);
                    entry.Offset = index.ReadUInt32();
                    entry.Size   = index.ReadUInt32();
                    entry.UnpackedSize = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    if (entry.Name.HasAnyOfExtensions ("CRGB", "CHAR", "rol", "edg"))
                        entry.Type = "image";
                    entry.IsPacked = (flags & 0xFF0000) != 0;
                    entry.StoredSize = (flags & 0x2000000) != 0;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PmDatEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            {
                var unpacked_size = pent.UnpackedSize;
                if (pent.StoredSize)
                {
                    unpacked_size = input.ReadUInt32();
                    pent.UnpackedSize = unpacked_size;
                }
                var output = new byte[unpacked_size];
                LzUnpack (input, output);
                return new BinMemoryStream (output);
            }
        }

        void LzUnpack (IBinaryStream input, byte[] output)
        {
            var frame = new byte[0x1000];
            int dst = 0;
            int bits = 0;
            int mask = 0;
            int frame_pos = 0xFEE;
            while (dst < output.Length)
            {
                mask >>= 1;
                if (0 == mask)
                {
                    bits = input.ReadByte();
                    if (-1 == bits)
                        break;
                    mask = 0x80;
                }
                if (0 == (bits & mask))
                {
                    int b = input.ReadByte();
                    if (-1 == b)
                        break;
                    output[dst++] = (byte)b;
                    frame[frame_pos++ & 0xFFF] = (byte)b;
                }
                else
                {
                    int offset = input.ReadUInt16();
                    int count = (offset & 0xF) + 3;
                    offset >>= 4;
                    while (count --> 0 && dst < output.Length)
                    {
                        byte v = frame[offset++ & 0xFFF];
                        frame[frame_pos++ & 0xFFF] = v;
                        output[dst++] = v;
                    }
                }
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            if (entry.Name.HasAnyOfExtensions (".BGD", ".CRGB"))
            {
                var input = OpenEntry (arc, entry);
                var info = new ImageMetaData { Width = 800, Height = 600, BPP = 24 };
                return new CgdReader (BinaryStream.FromStream (input, entry.Name), info);
            }
            else if (entry.Name.HasExtension (".edg"))
            {
                var input = OpenEntry (arc, entry);
                var info = new ImageMetaData { Width = 460, Height = 345, BPP = 24 };
                return new ImgReader (BinaryStream.FromStream (input, entry.Name), info);
            }
            else if (entry.Name.HasExtension (".rol"))
            {
                var input = OpenEntry (arc, entry);
                uint width = 202;
                uint height = (uint)input.Length / (((width * 3u) + 3u) & ~3u);
                var info = new ImageMetaData { Width = width, Height = height, BPP = 24 };
                return new ImgReader (BinaryStream.FromStream (input, entry.Name), info);
            }
            else if (entry.Name.HasExtension (".CHAR"))
            {
                var input = OpenEntry (arc, entry);
                var info = new ChrMetaData {
                    Width = 800, Height = 600, BPP = 32,
                    DataOffset = 0, RgbSize = (int)input.Length,
                };
                return new ChrReader (BinaryStream.FromStream (input, entry.Name), info);
            }
            return base.OpenImage (arc, entry);
        }
    }

    internal class ImgReader : BinaryImageDecoder
    {
        public ImgReader (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0;
            int stride = (int)Info.Width * Info.BPP / 8;
            stride = (stride + 3) & ~3;
            int total = stride * (int)Info.Height;
            var pixels = m_input.ReadBytes (total);
            if (pixels.Length != total)
                throw new EndOfStreamException();
            return ImageData.CreateFlipped (Info, PixelFormats.Bgr24, null, pixels, stride);
        }
    }
}
