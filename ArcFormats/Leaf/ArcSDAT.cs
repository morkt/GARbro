//! \file       ArcSDAT.cs
//! \date       2026-04-16
//! \brief      AQUAPLUS resource archive.
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
using System.Text;

namespace GameRes.Formats.Leaf {
    [Export(typeof(ArchiveFormat))]
    public class SDatOpener : ArchiveFormat {
        public override string         Tag => "SDAT";
        public override string Description => "AQUAPLUS engine resource archive";
        public override uint     Signature => 0x656C6946; // 'Filename'
        public override bool  IsHierarchic => true;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen(ArcView file) {
            if (!file.View.AsciiEqual(4, "name    "))
                return null; // TODO: it is said that only Steam ver has these spaces
            uint section_size = file.View.ReadUInt32(0x0C);
            var filenames = new List<KeyValuePair<uint, string>>();
            uint section_offset = 0x10, offset = section_offset;
            do {
                uint name_offset = file.View.ReadUInt32(offset) + section_offset;
                var name_buffer = new List<byte>();
                for (int i = 0; name_offset + i < section_size; i++) {
                    byte b = file.View.ReadByte(name_offset + i);
                    if (b == 0)
                        break;
                    name_buffer.Add(b);
                }
                string name = Encoding.UTF8.GetString(name_buffer.ToArray());
                if (name.StartsWith(@"\"))
                    name = name.Substring(1);
                filenames.Add(new KeyValuePair<uint, string>(name_offset, name));
                offset += 4;
            } while (offset < filenames[0].Key);

            uint section_start = (uint)((section_size + 7) & ~7);
            if (!file.View.AsciiEqual(section_start, "Pack        "))
                return null;
            uint count = file.View.ReadUInt32(section_start + 0x10);
            if (count != filenames.Count)
                return null;
            section_size = file.View.ReadUInt32(section_start + 0x0C);
            var dir = new List<Entry>(filenames.Count);
            offset = section_start + 0x14;
            for (int i = 0; i < count; i++) {
                var entry = Create<Entry>(filenames[i].Value);
                entry.Offset = file.View.ReadUInt32(offset);
                entry.Size = file.View.ReadUInt32(offset + 4);
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                if (entry.Name.HasExtension(".se") || entry.Name.HasExtension(".voice"))
                    entry.Type = "audio";
                dir.Add(entry);
                offset += 8;
            }
            return new ArcFile(file, this, dir);
        }
    }
}
