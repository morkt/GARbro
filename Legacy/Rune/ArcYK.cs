//! \file       ArcYK.cs
//! \date       2018 Mar 19
//! \brief      Rune resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

// [000317][Rune] Yuki no Kanata

namespace GameRes.Formats.Rune
{
    internal class YkArchive : ArcFile
    {
        public readonly int Key;

        public YkArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class YkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "YK"; } }
        public override string Description { get { return "Rune resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if ((file.View.ReadInt32 (0) | file.View.ReadInt32 (4) | file.View.ReadInt32 (8)) != 0)
                return null;
            int key = file.View.ReadInt32 (0x10);
            int count = file.View.ReadInt32 (0x14);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x18;
            var index = new Dictionary<int, Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                int id = file.View.ReadInt32 (index_offset);
                var entry = new Entry {
                    Offset = file.View.ReadUInt32 (index_offset+4),
                    Size   = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index[id] = entry;
                index_offset += 12;
            }
            var names_entry = index[0];
            var names = file.View.ReadBytes (names_entry.Offset, names_entry.Size);
            if (key != 0)
                DecryptData (names, key);
            using (var input = new BinMemoryStream (names))
            {
                while (input.PeekByte() != -1)
                {
                    int id = input.ReadInt32();
                    var name = input.ReadCString();
                    if (!string.IsNullOrEmpty (name))
                    {
                        var entry = index[id];
                        entry.Name = name;
                        entry.Type = FormatCatalog.Instance.GetTypeFromName (name);
                    }
                }
            }
            var dir = index.Values.Where (e => !string.IsNullOrEmpty (e.Name)).ToList();
            return new YkArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var yarc = arc as YkArchive;
            if (null == yarc || 0 == yarc.Key)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            DecryptData (data, yarc.Key);
            return new BinMemoryStream (data, entry.Name);
        }

        void DecryptData (byte[] data, int key)
        {
            for (int i = 0; i < data.Length; ++i)
            {
                uint shift = (uint)(92 * key * i * (i + key));
                data[i] = Binary.RotByteL (data[i], (int)(7 - shift % 7));
            }
        }
    }
}
