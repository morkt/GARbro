//! \file       ArcPAK.cs
//! \date       2017 Dec 09
//! \brief      System21 resource archive.
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

// [000114][Excellents] Thanksgiving Special
// [000615][Beenyan] Oyako Donburi
// [010511][Haoh] May Club DX

namespace GameRes.Formats.System21
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/SYSTEM21"; } }
        public override string Description { get { return "System21 engine resource archive"; } }
        public override uint     Signature { get { return 0x978FAD8F; } } // '少女'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Signatures = new uint[] { 0x978FAD8F, 0x798AF589 }; // '快楽'
        }

        static readonly byte[] NameSizes = { 0x14, 0x34, 0x64 };

        public override ArcFile TryOpen (ArcView file)
        {
            bool new_version = file.View.ReadUInt32 (0) == 0x798AF589u;
            uint data_offset = file.View.ReadUInt32 (4);
            int n = new_version ? 0 : 2;
            uint index_size = data_offset - 12;
            var dir = new List<Entry>();
            for (; n < NameSizes.Length; ++n)
            {
                uint name_size = NameSizes[n];
                if (index_size % (name_size + 4) != 0)
                    continue;
                int count = (int)(index_size / (name_size + 4));
                if (!IsSaneCount (count))
                    continue;

                dir.Clear();
                if (ReadIndex (file, count, data_offset, dir, name_size))
                    return new ArcFile (file, this, dir);
            }
            return null;
        }

        bool ReadIndex (ArcView file, int count, uint data_offset, List<Entry> dir, uint name_size)
        {
            uint index_offset = 12;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_size);
                if (string.IsNullOrEmpty (name))
                    return false;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset + name_size);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return false;
                dir.Add (entry);
                index_offset += name_size + 4;
                data_offset += entry.Size;
            }
            return true;
        }
    }
}
