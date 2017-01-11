//! \file       ArcAAR.cs
//! \date       Tue Jan 10 19:40:28 2017
//! \brief      AliceSoft resource archive.
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
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.AliceSoft
{
    [Export(typeof(ArchiveFormat))]
    public class AarOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AAR"; } }
        public override string Description { get { return "AliceSoft System engine resource archive"; } }
        public override uint     Signature { get { return 0x00524141; } } // 'AAR'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AarOpener ()
        {
            Extensions = new string[] { "red" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            using (var index = file.CreateStream())
            {
                index.Position = 0xC;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32();
                    uint size   = index.ReadUInt32();
                    bool is_packed = index.ReadInt32() != 1;
                    string name = index.ReadCString();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size = size;
                    entry.IsPacked = is_packed;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "ZLB\0"))
                return base.OpenEntry (arc, entry);
            var pent = entry as PackedEntry;
            if (null != pent && 0 == pent.UnpackedSize)
            {
                pent.IsPacked = true;
                pent.UnpackedSize = arc.File.View.ReadUInt32 (8);
            }
            uint packed_size = arc.File.View.ReadUInt32 (0xC);
            var input = arc.File.CreateStream (entry.Offset+0x10, packed_size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
