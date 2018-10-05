//! \file       ArcPAK2.cs
//! \date       Mon Oct 26 04:40:25 2015
//! \brief      Palette PACK2 resource archives.
//! \brief      
//
// Copyright (C) 2015-2018 by morkt
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

namespace GameRes.Formats.Palette
{
    [Export(typeof(ArchiveFormat))]
    public class Pak2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK2/Palette"; } }
        public override string Description { get { return "Palette resource archive"; } }
        public override uint     Signature { get { return 0x43415005; } } // '\x05PACK2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public Pak2Opener ()
        {
            Extensions = new string[] { "pak" };
            ContainedFormats = new[] { "PGA", "CHR/Palette", "OGG", "WAV" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "K2"))
                return null;
            int count = file.View.ReadInt32 (6);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 0xA;
            var name_buf = new byte[0x40];
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int name_length = file.View.ReadByte (index_offset++);
                if (name_length > name_buf.Length)
                    name_buf = new byte[name_length];
                file.View.Read (index_offset, name_buf, 0, (uint)name_length);
                for (int j = 0; j < name_length; ++j)
                    name_buf[j] ^= 0xFF;
                index_offset += (uint)name_length;
                var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size   = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
