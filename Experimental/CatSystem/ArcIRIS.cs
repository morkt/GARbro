//! \file       ArcIRIS.cs
//! \date       2026-01-25
//! \brief      CatSystem for Android resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using GameRes.Utility;

namespace GameRes.Formats.CatSystem {
    [Export(typeof(ArchiveFormat))]
    public class IrisPckOpener : ArchiveFormat {
        public override string         Tag { get { return "DAT/IRIS"; } }
        public override string Description { get { return "CatSystem for Android resource archive"; } }
        public override uint     Signature { get { return 0x53495249; } } // 'IRISPCK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file) {
            if (!file.View.AsciiEqual(4, "PCK"))
                return null;

            uint offset = 0x18;
            var dir = new List<Entry>();
            var name_buffer = new StringBuilder();

            while (offset < file.MaxOffset) {
                offset += 8;
                uint name_length = file.View.ReadUInt32(offset);

                offset += 8;
                name_buffer.Clear();
                for (int i = 0; i < name_length; i += 2) {
                    char c = (char)file.View.ReadUInt16(offset + i);
                    if (c == 0)
                        break;
                    name_buffer.Append(c);
                }
                var dirname = name_buffer.ToString().Replace("/", "\\");
                offset += name_length;

                offset += 4;
                int count = (int)file.View.ReadUInt32(offset);
                var dir_inner = new List<Entry>(count);

                offset += 0xC;
                uint prev = 0;
                for (int i = 0; i < count; i++) {
                    offset += 8;
                    uint size = file.View.ReadUInt32(offset);
                    uint padded_size = file.View.ReadUInt32(offset + 4);
                    name_length = file.View.ReadUInt32(offset + 8);
                    offset += 0x18;
                    name_buffer.Clear();
                    for (int j = 0; j < name_length; j += 2) {
                        char c = (char)file.View.ReadUInt16(offset + j);
                        if (c == 0)
                            break;
                        name_buffer.Append(c);
                    }
                    var basename = name_buffer.ToString();
                    var entry = Create<Entry>(Path.Combine(dirname, basename));
                    entry.Offset = prev;
                    entry.Size = size;
                    prev += padded_size;
                    offset += name_length;
                    dir_inner.Add(entry);
                }

                for (int i = 0; i < count; i++) {
                    dir_inner[i].Offset += offset;
                }
                offset += prev;
                dir.AddRange(dir_inner);
            }

            return new ArcFile(file, this, dir);
        }
    }
}
