//! \file       ArcACV.cs
//! \date       Wed Dec 28 09:49:14 2016
//! \brief      Mirai resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.NonColor
{
    [Export(typeof(ArchiveFormat))]
    public class AcvOpener : DatOpener
    {
        public override string         Tag { get { return "ACV"; } }
        public override string Description { get { return "Mirai resource archive"; } }
        public override uint     Signature { get { return 0x31564341; } } // 'ACV1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AcvOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint key = 0x8B6A4E5F;
            int count = file.View.ReadInt32 (4) ^ (int)key;
            if (!IsSaneCount (count))
                return null;

            var scheme = QueryScheme (file.Name);
            if (null == scheme)
                return null;

            bool is_script = VFS.IsPathEqualsToFileName (file.Name, "script.dat");

            var dir = new List<Entry> (count);
            using (var input = file.CreateStream (8, (uint)count * 0x15))
            {
                foreach (var entry in ReadIndex (input, count, scheme, key))
                {
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    if (is_script)
                        entry.Hash ^= scheme.Hash;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var dent = entry as ArcDatEntry;
            if (null == dent || 0 == dent.Size)
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            if (dent.IsPacked)
            {
                DecryptData (data, (uint)dent.Hash);
                return new ZLibStream (new MemoryStream (data), CompressionMode.Decompress);
            }
            if (dent.RawName != null && 0 != dent.Flags)
                DecryptWithName (data, dent.RawName);
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
