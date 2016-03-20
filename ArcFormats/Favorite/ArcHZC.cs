//! \file       ArcHZC.cs
//! \date       Wed Dec 09 17:04:23 2015
//! \brief      Favorite View Point multi-frame image format.
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
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.FVP
{
    internal class HzcArchive : ArcFile
    {
        public readonly HzcMetaData ImageInfo;

        public HzcArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, HzcMetaData info)
            : base (arc, impl, dir)
        {
            ImageInfo = info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class HzcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "HZC/MULTI"; } }
        public override string Description { get { return "Favorite View Point multi-frame image"; } }
        public override uint     Signature { get { return 0x31637A68; } } // 'HZC1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public HzcOpener ()
        {
            Extensions = new string[] { "hzc" };
        }

        static readonly Lazy<ImageFormat> Hzc = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("HZC"));

        public override ArcFile TryOpen (ArcView file)
        {
            uint header_size = file.View.ReadUInt32 (8);
            HzcMetaData image_info;
            using (var header = file.CreateStream (0, 0xC+header_size))
            {
                image_info = Hzc.Value.ReadMetaData (header) as HzcMetaData;
                if (null == image_info)
                    return null;
            }
            int count = file.View.ReadInt32 (0x20);
            if (0 == count)
                count = 1;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            int frame_size = image_info.UnpackedSize / count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D3}.tga", base_name, i),
                    Type = "image",
                    Offset = frame_size * i,
                    Size = 0x12 + (uint)frame_size,
                };
                dir.Add (entry);
            }
            return new HzcArchive (file, this, dir, image_info);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var hzc = (HzcArchive)arc;
            using (var input = arc.File.CreateStream (0xC+hzc.ImageInfo.HeaderSize))
            using (var z = new ZLibStream (input, CompressionMode.Decompress))
            {
                uint frame_size = entry.Size - 0x12;
                var pixels = new byte[frame_size];
                uint offset = 0;
                for (;;)
                {
                    if (pixels.Length != z.Read (pixels, 0, pixels.Length))
                        break;
                    if (offset >= entry.Offset)
                        break;
                    offset += frame_size;
                }
                if (4 == hzc.ImageInfo.Type)
                {
                    for (int i = 0; i < pixels.Length; ++i)
                        if (1 == pixels[i])
                            pixels[i] = 0xFF;
                }
                return TgaStream.Create (hzc.ImageInfo, pixels);
            }
        }
    }
}
