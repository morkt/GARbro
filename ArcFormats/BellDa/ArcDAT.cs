//! \file       ArcDAT.cs
//! \date       2017 Nov 24
//! \brief      BELL-DA resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Compression;

namespace GameRes.Formats.BellDa
{
    [Export(typeof(ArchiveFormat))]
    public class BldOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/BLD"; } }
        public override string Description { get { return "BELL-DA resource archive"; } }
        public override uint     Signature { get { return 0x30444C42; } } // 'BLD0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            var version_str = file.View.ReadString (4, 4).TrimEnd ('\x1A');
            if (version_str != "0" && version_str != "1" && version_str != "12" && version_str != "3")
                return null;
            int count = file.View.ReadInt16 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (0xA);
            if (index_offset >= file.MaxOffset)
                return null;
            uint data_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0xC);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size   = file.View.ReadUInt32 (index_offset+0xC);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x10;
                data_offset += entry.Size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            // XXX compression method identical to Maika.Mk2Opener
            var id_str = arc.File.View.ReadString (entry.Offset, 2);
            if (id_str != "B1" && id_str != "D1" && id_str != "E1")
                return base.OpenImage (arc, entry);
            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+2);
            if (packed_size != entry.Size - 10)
                return base.OpenImage (arc, entry);
            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset+6);
            using (var input = arc.File.CreateStream (entry.Offset+10, packed_size))
            using (var lzss = new LzssReader (input, (int)packed_size, (int)unpacked_size))
            {
                lzss.Unpack();
                var bmp = new BinMemoryStream (lzss.Data, entry.Name);
                return new ImageFormatDecoder (bmp);
            }
        }
    }
}
