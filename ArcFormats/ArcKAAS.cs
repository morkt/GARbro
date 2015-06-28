//! \file       ArcKAAS.cs
//! \date       Fri Apr 10 15:21:30 2015
//! \brief      KAAS engine archive format implementation.
//
// Copyright (C) 2015 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.KAAS
{
    [Export(typeof(ArchiveFormat))]
    public class PdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PD/KAAS"; } }
        public override string Description { get { return "KAAS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public PdOpener ()
        {
            Extensions = new string[] { "pd" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int index_offset = file.View.ReadByte (0);
            if (index_offset <= 2 || index_offset >= file.MaxOffset)
                return null;
            int key = file.View.ReadByte (1);
            int count = 0xfff & file.View.ReadUInt16 (index_offset);
            if (0 == count)
                return null;
            index_offset += 16;

            byte[] index = new byte[count*8];
            if (index.Length != file.View.Read (index_offset, index, 0, (uint)(index.Length)))
                return null;
            DecryptIndex (index, key);

            var dir = new List<Entry> (count);
            int data_offset = index_offset + index.Length;
            index_offset = 0;
            for (int i = 0; i < count; ++i)
            {
                uint offset = LittleEndian.ToUInt32 (index, index_offset);
                uint size   = LittleEndian.ToUInt32 (index, index_offset+4);
                if (offset < data_offset || offset >= file.MaxOffset)
                    return null;
                var entry = new Entry {
                    Name = string.Format ("{0:D4}.pic", i),
                    Type = "image",
                    Offset = offset,
                    Size = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        private void DecryptIndex (byte[] index, int key)
        {
            for (int i = 0; i != index.Length; ++i)
            {
                int k = i + 14;
                int r = 9 - (k & 7) * (k + 5) * key * 0x77;
//                int r = ((k * 0x6b) % (k / 2 + 1)) + key * 0x3b * (k + 11) * (k % (k + 17));
                index[i] -= (byte)r;
            }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PbOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PB"; } }
        public override string Description { get { return "KAAS engine resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool     CanCreate { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (count <= 0 || count > 0xfff)
                return null;
            var dir = new List<Entry> (count);
            int index_offset = 0x10;
            bool is_voice = Path.GetFileName (file.Name).Equals ("voice.pb", StringComparison.InvariantCultureIgnoreCase);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                Entry entry;
                if (!is_voice)
                    entry = AutoEntry.Create (file, offset, i.ToString ("D4"));
                else
                    entry = new Entry { Name = string.Format ("{0:D4}.pb", i), Type = "archive", Offset = offset };
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
