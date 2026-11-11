//! \file       ArcMIP.cs
//! \date       2026-01-16
//! \brief      Minori resource archive.
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
using GameRes.Utility;

namespace GameRes.Formats.Musica {
    [Export(typeof(ArchiveFormat))]
    public class MipOpener : ArchiveFormat {
        public override string         Tag { get { return "MIP"; } }
        public override string Description { get { return "Minori resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<string, uint[]> KnownGameMap = new Dictionary<string, uint[]> {
            { "BSF.exe", new uint[] { 0x42e100, 0x42f1d0 } }
        };

        public MipOpener() {
            Extensions = new string[] { "mip" };
        }

        public override ArcFile TryOpen(ArcView file) {
            string extension;
            uint[] sizes;

            string basename = Path.GetFileName(file.Name).ToLower();
            if (basename == "res.mip") {
                extension = ".png";
                sizes = GetSizesFromExe(file, 0);
            }
            else if (basename == "sc.mip") {
                extension = "";
                sizes = GetSizesFromExe(file, 1);
            }
            else
                return null;

            int count = sizes.Length;
            var dir = new List<Entry>(count);
            uint offset = 0;
            for (int i = 0; i < count; i++) {
                var entry = new Entry {
                    Name = i.ToString("D4") + extension,
                    Type = extension == "" ? "script" : "image",
                    Offset = offset,
                    Size = sizes[i]
                };
                if (!entry.CheckPlacement(file.MaxOffset))
                    return null;
                dir.Add(entry);
                offset += entry.Size;
            }

            return new ArcFile(file, this, dir);
        }

        uint[] GetSizesFromExe(ArcView file, int type) {
            foreach (var exe_name in KnownGameMap.Keys) {
                if (VFS.FileExists(exe_name)) {
                    using (var exe_file = VFS.OpenView(exe_name)) {
                        var exe = new ExeFile(exe_file);
                        long table_ofs = exe.GetAddressOffset(KnownGameMap[exe_name][type]);
                        var sizes = new List<uint>();
                        while (table_ofs < exe_file.MaxOffset) {
                            // skip offsets
                            uint size = exe_file.View.ReadUInt32(table_ofs + 4);
                            if (size == 0)
                                break;
                            sizes.Add(size);
                            table_ofs += 8;
                        }
                        return sizes.ToArray();
                    }
                }
            }
            throw new FileNotFoundException();
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            var s = new XoredStream(arc.File.CreateStream(entry.Offset, entry.Size), 0xFF);
            if (entry.Name.EndsWith(".png"))
                return new PrefixStream(PngFormat.HeaderBytes, s);
            return s;
        }
    }
}
