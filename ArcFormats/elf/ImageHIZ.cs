//! \file       ImageHIZ.cs
//! \date       Fri Jul 31 19:07:24 2015
//! \brief      elf HIZ bitmap format.
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
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Elf
{
    internal class HizMetaData : ImageMetaData
    {
        public bool IsPacked;
        public uint DataOffset;
        public uint UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class HizFormat : ImageFormat
    {
        public override string         Tag { get { return "HIZ"; } }
        public override string Description { get { return "elf bitmap format"; } }
        public override uint     Signature { get { return 0x007A6968; } } // 'hiz'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            int n = file.ReadInt32();
            if (100 != n)
                return null;
            uint right = file.ReadUInt32() ^ 0xAA5A5A5A;
            uint bottom = file.ReadUInt32() ^ 0xAC9326AF;
            int unknown1 = file.ReadInt32(); // @0x10
            if (unknown1 == 0x375A8436)
                return null;
            uint unpacked_size = file.ReadUInt32() ^ 0x19739D6A; // @0x14
            if (unpacked_size != right*bottom*4)
                return null;
            return new HizMetaData
            {
                Width = right,
                Height = bottom,
                BPP = 32,
                IsPacked = true,
                DataOffset = 0x4c,
                UnpackedSize = unpacked_size,
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (HizMetaData)info;

            var pixels = new byte[meta.UnpackedSize];
            stream.Position = meta.DataOffset;
            using (var lzss = new LzssStream (stream.AsStream, LzssMode.Decompress, true))
            {
                var channel = new byte[info.Width*info.Height];
                for (int p = 0; p < 4; ++p)
                {
                    if (channel.Length != lzss.Read (channel, 0, channel.Length))
                        throw new InvalidFormatException ("Unexpected end of file");
                    int src = 0;
                    for (int i = p; i < pixels.Length; i += 4)
                        pixels[i] = channel[src++];
                }
                return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HizFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class HipFormat : HizFormat
    {
        public override string         Tag { get { return "HIP"; } }
        public override string Description { get { return "elf composite image format"; } }
        public override uint     Signature { get { return 0x00706968; } } // 'hip'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x18);
            int index_offset = 0xC;
            uint first_offset = header.ToUInt32 (index_offset);
            if (0 == first_offset)
            {
                index_offset += 4;
                first_offset = header.ToUInt32 (index_offset);
                if (0 == first_offset)
                    return null;
            }
            index_offset += 4;

            long first_length;
            uint second_offset = header.ToUInt32 (index_offset);
            if (0 == second_offset)
                first_length = stream.Length - first_offset;
            else if (second_offset < first_offset)
                return null;
            else
                first_length = second_offset - first_offset;

            using (var reg = new StreamRegion (stream.AsStream, first_offset, first_length, true))
            using (var hiz = new BinaryStream (reg, stream.Name))
            {
                var info = base.ReadMetaData (hiz);
                (info as HizMetaData).DataOffset += 0x18;
                return info;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HipFormat.Write not implemented");
        }
    }
}
