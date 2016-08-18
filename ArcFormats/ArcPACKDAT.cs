//! \file       ArcInnGrey.cs
//! \date       Mon Jan 19 08:57:16 2015
//! \brief      Innocent Grey archives format.
//
// Copyright (C) 2015-2016 by morkt
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

using System.IO;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using GameRes.Utility;

namespace GameRes.Formats.SystemEpsylon
{
    internal class PackDatEntry : PackedEntry
    {
        public uint Flags;
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACKDAT"; } }
        public override string Description { get { return "SYSTEM-ε resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // "PACK"
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak", "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "DAT."))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_size = 0x30 * (uint)count;
            if (index_size > file.View.Reserve (0x10, index_size))
                return null;
            var dir = new List<Entry> (count);
            long index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                string name = file.View.ReadString (index_offset, 0x20);
                var entry = FormatCatalog.Instance.Create<PackDatEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x20);
                entry.Flags  = file.View.ReadUInt32 (index_offset+0x24);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x28);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x2c);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pentry = entry as PackDatEntry;
            if (null == pentry || entry.Size < 4 || 0 == (pentry.Flags & 0x10000))
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var input = arc.File.View.ReadBytes (entry.Offset, pentry.Size);
            if (input.Length == pentry.Size)
            {
                unsafe
                {
                    fixed (byte* buf_raw = input)
                    {
                        uint* encoded = (uint*)buf_raw;
                        uint key = pentry.Size >> 2;
                        key = (key << (((int)key & 7) + 8)) ^ key;
                        for (uint i = entry.Size / 4; i != 0; --i )
                        {
                            *encoded ^= key;
                            int cl = (int)(*encoded++ % 24);
                            key = Binary.RotL (key, cl);
                        }
                    }
                }
            }
            return new MemoryStream (input);
        }
    }
}
