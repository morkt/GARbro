//! \file       ArcFPD.cs
//! \date       2026-01-19
//! \brief      AGES Mk2 resource archive.
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Anchor {
    internal class FpdArchive : ArcFile {
        public readonly byte[] Key;

        public FpdArchive(ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key) : base (arc, impl, dir) {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class FpdOpener : ArchiveFormat {
        public override string         Tag { get { return "FPD"; } }
        public override string Description { get { return "AGES Mk2 resource archive"; } }
        public override uint     Signature { get { return 0x00445046; } } // 'FPD\x00'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen(ArcView file) {
            int version = Binary.BigEndian(file.View.ReadInt32(4));
            if (version != 2)
                return null;

            int count = (int)Binary.BigEndian(file.View.ReadInt64(8));
            if (!IsSaneCount(count))
                return null;

            long data_start = Binary.BigEndian(file.View.ReadInt64(0x10));
            long offset = 0x38;
            if (data_start < count * 32 + offset)
                return null;
            uint index_size = (uint)(data_start - offset);

            var key = DefaultScheme.ArchiveKey;
            var dir = new List<Entry>(count);

            using (var input = file.CreateStream(offset, (uint)(data_start - offset)))
            using (var decrypted = new ByteStringEncryptedStream(input, key))
            using (var reader = new BinaryReader(decrypted)) {
                var name_offsets = new List<long>(count);
                for (int i = 0; i < count; i++) {
                    long name_offset = Binary.BigEndian(reader.ReadInt64());
                    long data_offset = Binary.BigEndian(reader.ReadInt64());
                    long size = Binary.BigEndian(reader.ReadInt64());
                    long unpacked_size = Binary.BigEndian(reader.ReadInt64());

                    var entry = new PackedEntry {
                        Offset = data_start + data_offset,
                        Size = (uint)size,
                        UnpackedSize = (uint)unpacked_size,
                        IsPacked = unpacked_size != 0
                    };
                    if (!entry.CheckPlacement(file.MaxOffset))
                        return null;
                    dir.Add(entry);
                    name_offsets.Add(name_offset);
                }
                var name_block = reader.ReadBytes((int)(data_start - offset - count * 32));
                using (var mem = new MemoryStream(name_block))
                using (var stream = new ZLibStream(mem, CompressionMode.Decompress))
                using (var output = new MemoryStream()) {
                    stream.CopyTo(output);
                    var names = output.ToArray();
                    for (int i = 0; i < count; i++) {
                        dir[i].Name = Binary.GetCString(names, (int)name_offsets[i], Encoding.UTF8);
                        dir[i].Type = FormatCatalog.Instance.GetTypeFromName(dir[i].Name);
                        if (dir[i].Name.EndsWith(".epk"))
                            dir[i].Type = "script";
                    }
                }
                return new FpdArchive(file, this, dir, key);
            }
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry) {
            var farc = arc as FpdArchive;
            var pent = entry as PackedEntry;
            Stream input = farc.File.CreateStream(entry.Offset, entry.Size);
            input = new ByteStringEncryptedStream(input, farc.Key);
            if (pent.IsPacked)
                input = new ZLibStream(input, CompressionMode.Decompress);
            if (pent.Name.EndsWith(".epk")) {
                var mem = new MemoryStream();
                input.CopyTo(mem);
                mem.Seek(-0x20, SeekOrigin.End);
                var buf = new byte[4];
                mem.Read(buf, 0, 4);
                uint last = Binary.BigEndian(BitConverter.ToUInt32(buf, 0));
                mem.Seek(0, SeekOrigin.Begin);
                
                var key = Encoding.UTF8.GetBytes(Path.GetFileNameWithoutExtension(pent.Name));
                var encryption = new Mk2Blowfish(key, DefaultScheme.EpkContext);
                input = new InputCryptoStream(mem, encryption.CreateDecryptor());
                input = new LimitStream(input, last);
            }
            return input;
        }

        FpdScheme DefaultScheme = new FpdScheme();

        public override ResourceScheme Scheme {
            get { return DefaultScheme; }
            set { DefaultScheme = (FpdScheme)value; }
        }
    }

    [Serializable]
    public class FpdScheme : ResourceScheme {
        public byte[] ArchiveKey;
        public byte[] EpkContext;
    }
}
