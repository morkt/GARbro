//! \file       ArcEVB.cs
//! \date       2025-12-27
//! \brief      Enigma Virtual Box resource archive.
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
using System.Linq;
using System.Text;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.Enigma {
    public enum NodeTypes {
        AbsoluteDrive = 1,
        File = 2,
        Folder = 3,
    }

    [Export(typeof(ArchiveFormat))]
    public class EvbPackOpener : ArchiveFormat {
        public override string         Tag { get { return "EVB"; } }
        public override string Description { get { return "Enigma Virtual Box resource archive"; } }
        public override uint     Signature { get { return 0x425645; } } // 'EVB'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public EvbPackOpener() {
            Signatures = new uint[] { 0x425645, 0x905a4d, 0 };
            Extensions = new[] { "exe" };
        }

        public override ArcFile TryOpen(ArcView file) {
            uint base_offset = 0;
            if (file.View.AsciiEqual(0, "MZ")) {
                var exe = new ExeFile(file);
                var sig = new byte[] { 0x45, 0x56, 0x42, 0x00 };
                if (exe.ContainsSection(".enigma1")) {
                    var ofs = exe.FindString(exe.Sections[".enigma1"], sig);
                    if (ofs != -1)
                        base_offset = (uint)ofs;
                }
                if (base_offset == 0)
                    return null;
            }
            else if (!file.View.AsciiEqual(0, "EVB"))
                return null;

            uint index_size = file.View.ReadUInt32(base_offset + 0x40) + base_offset + 68;
            uint index_offset = base_offset + 0x4F;
            uint file_offset = index_size;

            var dir = new List<Entry>();
            var name_buffer = new StringBuilder();
            var counts = new List<uint> { file.View.ReadUInt32(base_offset + 0x4C) };
            var names = new List<string> { "" };

            while (index_offset < index_size - 4) {
                uint item_count = file.View.ReadUInt32(index_offset + 12);
                index_offset += 16;
                name_buffer.Clear();
                while (true) {
                    char c = (char)file.View.ReadUInt16(index_offset);
                    index_offset += 2;
                    if (c == 0)
                        break;
                    name_buffer.Append(c);
                }
                if (name_buffer.Length == 0)
                    return null;
                var name = name_buffer.ToString();
                var type = (NodeTypes)file.View.ReadByte(index_offset);
                index_offset++;
                counts[counts.Count - 1]--;
                if (type == NodeTypes.File) {
                    var entry = Create<PackedEntry>(Path.Combine(names.Concat(new[] { name }).ToArray()));
                    uint unpacked_size = file.View.ReadUInt32(index_offset + 2);
                    uint size = file.View.ReadUInt32(index_offset + 49);
                    entry.IsPacked = unpacked_size != size;
                    entry.Offset = file_offset;
                    entry.UnpackedSize = unpacked_size;
                    entry.Size = size;
                    file_offset += size;
                    if (!entry.CheckPlacement(file.MaxOffset))
                        return null;
                    dir.Add(entry);
                    index_offset += 53;
                }
                else if (type == NodeTypes.Folder) {
                    counts.Add(item_count);
                    names.Add(name);
                    index_offset += 25;
                }
                else if (type == NodeTypes.AbsoluteDrive) {
                    counts.Add(item_count);
                    names.Add(name[0].ToString());
                    index_offset -= 4;
                }
                else
                    return null;
                while (counts.Count > 0 && counts[counts.Count - 1] == 0) {
                    counts.RemoveAt(counts.Count - 1);
                    names.RemoveAt(names.Count - 1);
                }
            }

            return new ArcFile(file, this, dir);
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            var pent = entry as PackedEntry;
            if (pent.IsPacked) {
                uint header_size = arc.File.View.ReadUInt32(pent.Offset);
                uint offset = header_size;
                Stream input = null;

                for (uint i = 8; i < header_size; i += 12) {
                    uint chunk_size = arc.File.View.ReadUInt32(pent.Offset + i);
                    var chunk = new aPLibStream(
                        arc.File.CreateStream(pent.Offset + offset, chunk_size)
                    );
                    if (input != null)
                        input = new ConcatStream(input, chunk);
                    else
                        input = chunk;
                    offset += chunk_size;
                }

                return input;
            }
            else
                return arc.File.CreateStream(pent.Offset, pent.Size);
        }
    }
}
