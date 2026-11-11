//! \file       ArcDAT.cs
//! \date       2026-01-24
//! \brief      FOG resource archive format.
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
using GameRes.Utility;

namespace GameRes.Formats.Fog {
    internal class DatEntry : Entry {
        public string FileName;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat {
        public override string         Tag { get { return "DAT/FOG"; } }
        public override string Description { get { return "FOG resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file) {
            var base_name = Path.GetFileNameWithoutExtension(file.Name);
            bool multipart = base_name.Contains("_");
            if (multipart)
                base_name = base_name.Split('_')[0];
            var index_file_name = base_name + "File.dat";
            if (!File.Exists(index_file_name))
                return null;

            var index = File.ReadAllBytes(index_file_name);
            var transformer = new NotTransform();
            transformer.TransformBlock(index, 0, index.Length, index, 0);

            using (var mem = new MemoryStream(index))
            using (var reader = new BinaryReader(mem)) {
                var dir = new List<Entry>();

                while (mem.Position < mem.Length) {
                    uint name_length = Binary.BigEndian(reader.ReadUInt32());
                    string name = Binary.GetCString(reader.ReadBytes((int)name_length), 0);
                    var entry = Create<DatEntry>(name);
                    if (multipart) {
                        uint part = Binary.BigEndian(reader.ReadUInt32());
                        entry.FileName = string.Format("{0}_{1:00}.dat", base_name, part);
                        if (!File.Exists(entry.FileName))
                            return null;
                    }
                    else {
                        entry.FileName = file.Name;
                    }
                    reader.ReadUInt32();
                    entry.Offset = Binary.BigEndian(reader.ReadUInt32());
                    reader.ReadUInt32();
                    entry.Size = Binary.BigEndian(reader.ReadUInt32());
                    dir.Add(entry);
                }

                return new ArcFile(file, this, dir);
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            var dent = entry as DatEntry;
            using (var data_file = new ArcView(dent.FileName)) {
                var input = data_file.CreateStream(dent.Offset, dent.Size);
                return new XoredStream(input, 0xFF);
            }
        }
    }
}
