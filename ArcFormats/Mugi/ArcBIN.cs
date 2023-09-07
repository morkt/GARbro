//! \file       ArcBIN.cs
//! \date       2023 Sep 03
//! \brief      Mugi's resource archive.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [070323][schoolzone] Cosplay! Kyonyuu Mahjong

namespace GameRes.Formats.Mugi
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get => "BIN/MUGI"; }
        public override string Description { get => "Mugi's resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            const uint index_size = 0x8000 + 0x4000;
            if (file.MaxOffset <= index_size)
                return null;
            file.View.Reserve (0, index_size);
            uint index_pos = 0x8000;
            uint offset = file.View.ReadUInt32 (index_pos);
            if (offset != index_size)
                return null;
            uint[] offsets = new uint[0x800];
            int count = 0;
            while (offset != file.MaxOffset)
            {
                if (count == offsets.Length)
                    return null;
                offsets[count++] = offset;
                index_pos += 4;
                offset = file.View.ReadUInt32 (index_pos);
                if (offset < offsets[count-1] || offset > file.MaxOffset)
                    return null;
            }
            offsets[count--] = offset;
            uint name_pos = 0;
            uint size_pos = 0xA000;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (name_pos, 0x10);
                var entry = Create<PackedEntry> (name);
                entry.Offset = offsets[i];
                entry.Size = (uint)(offsets[i+1] - offsets[i]);
                entry.UnpackedSize = file.View.ReadUInt32 (size_pos);
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                name_pos += 0x10;
                size_pos += 4;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }
    }
}
