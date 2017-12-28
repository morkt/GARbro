//! \file       ArcPAK.cs
//! \date       2017 Dec 26
//! \brief      Morning encrypted resource archive.
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
using System.Diagnostics;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Morning
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/MORNING"; } }
        public override string Description { get { return "Morning encrypted resource archive"; } }
        public override uint     Signature { get { return 0x58668F8B; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            uint block_count = file.View.ReadUInt32 (4);
            var key = QueryKey (file.Name);
            if (null == key)
                return null;
            var index = file.View.ReadBytes (8, block_count * 0x200u);
            DecryptIndex (index, key);

            var dir = new List<Entry>();
            int block_offset = 0;
            for (uint i = 0; i < block_count; ++i)
            {
                int count = index.ToInt32 (block_offset);
                if (!IsSaneCount (count))
                    return null;
                int current_entry = block_offset + 4;
                for (int j = 0; j < count; ++j)
                {
                    int name_offset = index.ToInt32 (current_entry);
                    var name = Binary.GetCString (index, block_offset + name_offset);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.ToUInt32 (current_entry+4);
                    entry.Size   = index.ToUInt32 (current_entry+8);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    current_entry += 12;
                }
                block_offset += 0x200;
            }
            return new ArcFile (file, this, dir);
        }

        static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        static readonly byte[] PngIHdr   = { 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size <= 8 || !arc.File.View.BytesEqual (entry.Offset, PngIHdr))
                return base.OpenEntry (arc, entry);
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new PrefixStream (PngHeader, input);
        }

        void DecryptIndex (byte[] data, byte[] key)
        {
            int index_mask = key.Length-1;
            Debug.Assert ((key.Length & index_mask) == 0);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= key[i & index_mask];
            }
        }

        byte[] QueryKey (string arc_name)
        {
            return DefaultScheme.DefaultKey;
        }

        MorningScheme DefaultScheme = new MorningScheme();

        public override ResourceScheme Scheme
        {
            get { return DefaultScheme; }
            set { DefaultScheme = (MorningScheme)value; }
        }
    }

    [Serializable]
    public class MorningScheme : ResourceScheme
    {
        public byte[] DefaultKey;
    }
}
