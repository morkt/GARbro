//! \file       ArcMCA.cs
//! \date       Sun Dec 06 19:12:34 2015
//! \brief      F&C Co. multi-frame image format.
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

using GameRes.Formats.Properties;
using GameRes.Formats.Strings;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.FC01
{
    internal class McaArchive : ArcFile
    {
        public readonly byte Key;

        public McaArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class McaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MCA"; } }
        public override string Description { get { return "F&C Co. multi-frame image format"; } }
        public override uint     Signature { get { return 0x2041434D; } } // 'MCA'
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_offset = file.View.ReadUInt32 (0x10);
            int count = file.View.ReadInt32 (0x20);
            if (index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;

            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            long next_offset = file.View.ReadUInt32 (index_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (next_offset > file.MaxOffset || next_offset <= index_offset)
                    return null;
                index_offset += 4;
                var entry = new Entry
                {
                    Name    = string.Format ("{0}#{1:D4}.tga", base_name, i),
                    Offset  = next_offset,
                    Type    = "image",
                };
                next_offset = i+1 == count ? file.MaxOffset : file.View.ReadUInt32 (index_offset);
                entry.Size = (uint)(next_offset - entry.Offset);
                if (entry.Size > 0x20)
                    dir.Add (entry);
            }
            if (0 == dir.Count)
                return null;
            var options = Query<McgOptions> (arcStrings.MCAEncryptedNotice);
            return new McaArchive (file, this, dir, options.Key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var mca = arc as McaArchive;
            int method = arc.File.View.ReadInt32 (entry.Offset);
            if (null == mca || method < 0 || method > 1)
                return base.OpenEntry (arc, entry);
            uint width  = arc.File.View.ReadUInt32 (entry.Offset+0xC);
            uint height = arc.File.View.ReadUInt32 (entry.Offset+0x10);
            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+0x14);
            int unpacked_size = arc.File.View.ReadInt32 (entry.Offset+0x18);

            var data = arc.File.View.ReadBytes (entry.Offset+0x20, packed_size);
            MrgOpener.Decrypt (data, 0, data.Length, mca.Key);
            if (method > 0)
            {
                using (var input = new MemoryStream (data))
                using (var lzss = new MrgLzssReader (input, data.Length, unpacked_size))
                {
                    lzss.Unpack();
                    data = lzss.Data;
                }
            }
            int stride = ((int)width * 3 + 3) & ~3;
            var info = new ImageMetaData { Width = width, Height = height, BPP = 24 };
            return TgaStream.Create (info, stride, data);
        }

        public override ResourceOptions GetDefaultOptions ()
        {
            return new McgOptions { Key = Settings.Default.MCGLastKey };
        }

        public override ResourceOptions GetOptions (object widget)
        {
            var w = widget as GUI.WidgetMCG;
            if (null != w)
                Settings.Default.MCGLastKey = w.GetKey();
            return GetDefaultOptions();
        }

        public override object GetAccessWidget ()
        {
            return new GUI.WidgetMCG();
        }
    }
}
