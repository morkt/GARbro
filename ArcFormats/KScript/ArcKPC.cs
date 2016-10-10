//! \file       ArcKPC.cs
//! \date       Wed Aug 31 11:06:51 2016
//! \brief      KScript engine resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.KScript
{
    [Export(typeof(ArchiveFormat))]
    public class KpcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KPC"; } }
        public override string Description { get { return "KScript engine resource archive"; } }
        public override uint     Signature { get { return 0x50524353; } } // 'SCRPACK1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ACK1"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_size = file.View.ReadUInt32 (0xC);
            var index = file.View.ReadBytes (0x20, index_size);
            if (index.Length != index_size)
                return null;
            for (int i = 0; i < index.Length; ++i)
                index[i] ^= 0x45;

            int index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = Binary.GetCString (index, index_pos, 0x18);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                index_pos += 0x18;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_pos);
                entry.Size   = LittleEndian.ToUInt32 (index, index_pos+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
