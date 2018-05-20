//! \file       ArcPKD.cs
//! \date       2018 May 16
//! \brief      AdvDX engine resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.AdvDx
{
    internal class PkdArchive : ArcFile
    {
        public readonly byte Key;

        public PkdArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PkdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PKD"; } }
        public override string Description { get { return "AdvDX engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            byte key = file.View.ReadByte (0x87);
            using (var input = file.CreateStream (8, 0x88 * (uint)count))
            using (var dec = new XoredStream (input, key))
            using (var index = new BinaryStream (dec, file.Name))
            {
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x80);
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size   = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new PkdArchive (file, this, dir, key);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pkd = arc as PkdArchive;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == pkd || 0 == pkd.Key)
                return input;
            return new XoredStream (input, pkd.Key);
        }
    }
}
