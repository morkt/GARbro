//! \file       ImageFG.cs
//! \date       2018 Nov 05
//! \brief      FrontWing image format.
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

using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.FrontWing
{
    internal class FgMetaData : ImageMetaData
    {
        public uint DataOffset;
        public uint DataLength;
    }

    [Export(typeof(ImageFormat))]
    public class FwgiFormat : ImageFormat
    {
        public override string         Tag { get { return "FG/FWGI"; } }
        public override string Description { get { return "FrontWing image format"; } }
        public override uint     Signature { get { return 0x49475746; } } // 'FWGI'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x1B0);
            if (header.ToInt32 (4) != 1)
                return null;
            return new FgMetaData {
                Width = header.ToUInt32 (0x1C),
                Height = header.ToUInt32 (0x20),
                OffsetX = header.ToInt32 (0x0C),
                OffsetY = header.ToInt32 (0x10),
                BPP = 32,
                DataOffset = header.ToUInt32 (0x128) + 4,
                DataLength = header.ToUInt32 (0x12C),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (FgMetaData)info;
            using (var input = new StreamRegion (file.AsStream, meta.DataOffset, meta.DataLength, true))
            using (var bmp = new BinaryStream (input, file.Name))
            {
                var bmp_info = Bmp.ReadMetaData (bmp);
                if (null == bmp_info)
                    throw new InvalidFormatException();
                bmp.Position = 0;
                bmp_info.OffsetX = meta.OffsetX;
                bmp_info.OffsetY = meta.OffsetY;
                return Bmp.Read (bmp, bmp_info);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FwgiFormat.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class FweiFormat : ImageFormat
    {
        public override string         Tag { get { return "FG/FWEI"; } }
        public override string Description { get { return "FrontWing encoded image format"; } }
        public override uint     Signature { get { return 0x49455746; } } // 'FWEI'

        static readonly ResourceInstance<ImageFormat> Fwgi = new ResourceInstance<ImageFormat> ("FG/FWGI");

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var fge_name = Path.ChangeExtension (file.Name, ".fge");
            if (!VFS.FileExists (fge_name))
                return null;
            using (var fg = OpenFg (file))
            {
                return Fwgi.Value.ReadMetaData (fg);
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var fg = OpenFg (file))
            {
                return Fwgi.Value.Read (fg, info);
            }
        }

        internal IBinaryStream OpenFg (IBinaryStream fg)
        {
            var fge_name = Path.ChangeExtension (fg.Name, ".fge");
            using (var fge = VFS.OpenView (fge_name))
            {
                if (fge.MaxOffset != 0x818)
                    throw new InvalidFormatException();
                int chunk1_size = fge.View.ReadInt32 (0);
                int chunk2_offset = fge.View.ReadInt32 (0x404);
                int chunk2_size = fge.View.ReadInt32 (0x408);
                bool is_compressed = fge.View.ReadInt32 (0x810) != 0;
                int part1_size = chunk2_offset + chunk2_size;
                var part1 = new byte[part1_size];
                fge.View.Read (4, part1, 0, (uint)chunk1_size);
                fg.Position = 5;
                fg.Read (part1, chunk1_size, chunk2_offset - chunk1_size);
                fge.View.Read (0x40C, part1, chunk2_offset, (uint)chunk2_size);
                var part2 = new StreamRegion (fg.AsStream, fg.Position, true);
                Stream fg_stream = new PrefixStream (part1, part2);
                if (is_compressed)
                {
                    fg_stream = new ZLibStream (fg_stream, CompressionMode.Decompress);
                    fg_stream = new SeekableStream (fg_stream);
                }
                return new BinaryStream (fg_stream, fg.Name);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("FweiFormat.Write not implemented");
        }
    }
}
