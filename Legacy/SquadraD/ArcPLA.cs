//! \file       ArcPLA.cs
//! \date       2023 Sep 26
//! \brief      Squadra D audio archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.SquadraD
{
    internal class PlaEntry : PackedEntry
    {
        public  int Id;
        public  int n1;
        public uint SampleRate;
        public  int Channels;
        public byte n2;
        public byte n3;
        public int[] Data;
    }

    [Export(typeof(ArchiveFormat))]
    public class PlaOpener : ArchiveFormat
    {
        public override string         Tag => "PLA";
        public override string Description => "Squadra D audio archive";
        public override uint     Signature => 0x2E616C50; // 'Pla.'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            uint arc_size = file.View.ReadUInt32 (4);
            if (arc_size != file.MaxOffset || file.View.ReadUInt32 (0x10) != 2)
                return null;
            uint check = (arc_size & 0xD5555555u) << 1 | arc_size & 0xAAAAAAAAu;
            if (check != file.View.ReadUInt32 (8))
                return null;
            int count = file.View.ReadUInt16 (0xE);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            using (var index = file.CreateStream())
            {
                index.Position = 0x14;
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PlaEntry {
                        Id = index.ReadInt32()
                    };
                    entry.Name = entry.Id.ToString ("D5");
                    dir.Add (entry);
                }
                foreach (PlaEntry entry in dir)
                {
                    entry.n1 = index.ReadInt32();
                    entry.SampleRate = index.ReadUInt32();
                    entry.Channels = index.ReadInt32();
                    entry.n2 = index.ReadUInt8();
                    entry.n3 = index.ReadUInt8();
                    index.ReadInt16();
                }
                foreach (PlaEntry entry in dir)
                {
                    entry.Offset = index.ReadUInt32();
                }
                foreach (PlaEntry entry in dir)
                {
                    int n = entry.Channels * 2;
                    entry.Data = new int[n];
                    for (int j = 0; j < n; ++j)
                        entry.Data[j] = index.ReadInt32();
                }
            }
            long last_offset = file.MaxOffset;
            for (int i = dir.Count - 1; i >= 0; --i)
            {
                dir[i].Size = (uint)(last_offset - dir[i].Offset);
                last_offset = dir[i].Offset;
            }
            return new ArcFile (file, this, dir);
        }

        /*
        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return base.OpenEntry (arc, entry);
        }
        */
    }
}
