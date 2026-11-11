//! \file       ArcPCK.cs
//! \date       2026-07-05
//! \brief      Flix engine resource archive.
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
using System.Text;

namespace GameRes.Formats.RealLive
{
    [Export(typeof(ArchiveFormat))]
    public class PckOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PCK/FLIX"; } }
        public override string Description { get { return "Flix engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PckOpener ()
        {
            Extensions = new string[] { "pck" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (0);
            if (version != 1)
                return null;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint offset = 0x20;
            uint data_offset = file.View.ReadUInt32 (8) + offset;
            uint pos_offset = file.View.ReadUInt32 (0xC) + offset;
            if (data_offset >= file.MaxOffset || pos_offset >= file.MaxOffset)
                return null;

            var dir = new List<Entry> (count);
            var name_lengths = new uint[count];
            for (int i = 0; i < count; i++)
            {
                name_lengths[i] = file.View.ReadUInt32 (offset);
                offset += 4;
            }
            for (int i = 0; i < count; i++)
            {
                uint name_length = name_lengths[i];
                var name_buf = file.View.ReadBytes (offset, name_length);
                var entry = Create<Entry> (Encoding.Unicode.GetString (name_buf, 0, (int)name_length));
                dir.Add (entry);
                offset += name_length;
            }
            offset = pos_offset;
            for (int i = 0; i < count; i++)
            {
                dir[i].Offset = file.View.ReadUInt32 (offset); // uint64
                dir[i].Size = file.View.ReadUInt32 (offset + 8);
                if (!dir[i].CheckPlacement (file.MaxOffset))
                    return null;
                offset += 0x10;
            }

            return new ArcFile (file, this, dir);
        }
    }
}
