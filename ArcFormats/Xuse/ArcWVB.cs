//! \file       ArcWVB.cs
//! \date       2023 Oct 17
//! \brief      Xuse audio resource archive.
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

using GameRes.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class WvbOpener : ArchiveFormat
    {
        public override string         Tag => "WVB";
        public override string Description => "Xuse audio resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int first_offset = file.View.ReadInt32 (4) - 1;
            if (first_offset < 8 || first_offset >= file.MaxOffset)
                return null;
            int count = first_offset / 8;
            if ((first_offset & 7) != 0 || !IsSaneCount (count))
                return null;
            uint fmt_size = file.View.ReadUInt32 (first_offset);
            if (!file.View.AsciiEqual (first_offset+fmt_size+4, "data"))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_pos = 0;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_pos+4);
                if (0 == offset)
                    break;
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D2}", base_name, i),
                    Type = "audio",
                    Size = file.View.ReadUInt32 (index_pos) - 8,
                    Offset = offset - 1,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Size += 16;
                dir.Add (entry);
                index_pos += 8;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var header = new byte[0x10];
            LittleEndian.Pack (AudioFormat.Wav.Signature, header, 0);
            LittleEndian.Pack (entry.Size - 8u, header, 4);
            LittleEndian.Pack (0x45564157, header, 8);  // 'WAVE'
            LittleEndian.Pack (0x20746d66, header, 12); // 'fmt '
            var data = arc.File.CreateStream (entry.Offset, entry.Size - 16);
            return new PrefixStream (header, data);
        }
    }
}
