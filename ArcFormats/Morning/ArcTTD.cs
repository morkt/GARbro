//! \file       ArcTTD.cs
//! \date       Fri Aug 05 16:59:47 2016
//! \brief      Morning resource archives.
//
// Copyright (C) 2016 by morkt
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
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Morning
{
    [Export(typeof(ArchiveFormat))]
    public class TtdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TTD"; } }
        public override string Description { get { return "Morning resource archive"; } }
        public override uint     Signature { get { return 0x4352462E; } } // '.FRC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            uint key = file.View.ReadUInt32 (4);
            int index_size = count * 0x2C;
            var index = file.View.ReadBytes (0x14, (uint)index_size);
            if (index.Length != index_size)
                return null;
            Decrypt (index, key);
            int index_offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_offset+12, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x2C;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 8 || !arc.File.View.AsciiEqual (entry.Offset, "DSFF"))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            var lzss = new LzssStream (input);
            lzss.Config.FrameInitPos = 0xFF0;
            return lzss;
        }

        unsafe void Decrypt (byte[] data, uint key)
        {
            fixed (byte* data8 = data)
            {
                uint* data32 = (uint*)data8;
                for (int length = data.Length / 4; length > 0; --length)
                    *data32++ ^= key;
            }
        }
    }
}
