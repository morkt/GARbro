//! \file       ArcAC.cs
//! \date       2026-08-01
//! \brief      DEEP BLUE resource archive.
//
// Copyright (C) 2026 by morkt
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

namespace GameRes.Formats.DeepBlue
{
    [Export(typeof(ArchiveFormat))]
    public class AcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AC"; } }
        public override string Description { get { return "DEEP BLUE resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public AcOpener ()
        {
            Extensions = new string[] { "ac1", "ac2", "ac3", "ac4", "ac5" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (4);
            uint data_offset = file.View.ReadUInt32 (8);
            if (index_offset + count * 0x64 != data_offset)
                return null;

            var index = file.View.ReadBytes (index_offset, (uint)count * 0x64);
            for (int i = 0; i < index.Length; i++)
            {
                index[i] = Binary.RotByteL ((byte)(index[i] ^ 0xFF), 4);
            }

            int offset = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; i++)
            {
                var name = Binary.GetCString (index, offset, 0x40);
                var entry = Create<PackedEntry> (name);
                entry.Size         = LittleEndian.ToUInt32 (index, offset + 0x40);
                entry.Offset       = LittleEndian.ToUInt32 (index, offset + 0x44) + data_offset;
                entry.IsPacked     = LittleEndian.ToUInt32 (index, offset + 0x48) == 1;
                entry.UnpackedSize = LittleEndian.ToUInt32 (index, offset + 0x4C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                offset += 0x64;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
