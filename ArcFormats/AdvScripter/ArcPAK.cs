//! \file       ArcPAK.cs
//! \date       2018 Jan 31
//! \brief      ADVScripter resource archive.
//
// Copyright (C) 2018 by morkt
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
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.AdvScripter
{
    internal class MdArchive : ArcFile
    {
        public readonly int Version;

        public MdArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, int version)
            : base (arc, impl, dir)
        {
            Version = version;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/MD002"; } }
        public override string Description { get { return "ADVScripter engine resource archive"; } }
        public override uint     Signature { get { return 0x3030444D; } } // 'MD002'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadByte (4) != '2' || !file.View.AsciiEqual (0x21, "00V"))
                return null;
            int count = file.View.ReadInt32 (0x24);
            if (!IsSaneCount (count))
                return null;
            int version = file.View.ReadByte (0x20) - '0';
            if (version < 1 || version > 9)
                return null;
            var index_entry = new IndexReader (file, version);
            var buffer = new byte[0x30];
            uint index_offset = 0x28;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, buffer, 0, 0x30);
                index_entry.Decrypt (buffer);
                var name = Binary.GetCString (buffer, 0, 0x20);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset       = index_entry.Offset;
                entry.UnpackedSize = index_entry.UnpackedSize;
                entry.Size         = index_entry.PackedSize;
                entry.IsPacked     = buffer.ToInt32 (0x20) != 0;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new MdArchive (file, this, dir, version);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var mdarc = arc as MdArchive;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (mdarc != null && (mdarc.Version >= 5 && mdarc.Version <= 8))
                input = new XoredStream (input, 0xFF);
            var pent = entry as PackedEntry;
            if (pent != null && pent.IsPacked)
                input = new LzssStream (input);
            return input;
        }

        internal class IndexReader
        {
            public uint Offset;
            public uint PackedSize;
            public uint UnpackedSize;

            public readonly Action<byte[]> Decrypt;

            private uint m_key;

            public IndexReader (ArcView file, int version)
            {
                if (1 == version || 5 == version)
                {
                    Decrypt = buffer => {
                        Offset       = buffer.ToUInt32 (0x24);
                        UnpackedSize = buffer.ToUInt32 (0x28);
                        PackedSize   = buffer.ToUInt32 (0x2C);
                    };
                    return;
                }
                else if (2 == version || 6 == version)
                    m_key = uint.MaxValue;
                else
                    m_key = file.View.ReadUInt32 (0x1C);

                Action<byte[]> read = buffer => {
                    Offset       = buffer.ToUInt32 (0x24) ^ m_key;
                    UnpackedSize = buffer.ToUInt32 (0x28) ^ m_key;
                    PackedSize   = buffer.ToUInt32 (0x2C) ^ m_key;
                };
                Action transform;
                if (9 == version)
                {
                    transform = () => {
                        Offset       = (Offset       & 0xFFFF) << 15 | Offset       >> 17;
                        UnpackedSize = (UnpackedSize & 0xFFFF) << 14 | UnpackedSize >> 18;
                        PackedSize   = (PackedSize   & 0xFFFF) << 13 | PackedSize   >> 19;
                    };
                }
                else
                {
                    transform = () => {
                        Offset       >>= 1;
                        UnpackedSize >>= 2;
                        PackedSize   >>= 3;
                    };
                }

                Action<byte[]> decrypt_name = buffer => { };
                if (9 == version || 4 == version || 8 == version)
                {
                    var key_bytes = new byte[4];
                    LittleEndian.Pack (m_key, key_bytes, 0);
                    decrypt_name = buffer => {
                        for (int i = 0; i < 28; ++i)
                            buffer[i] ^= key_bytes[i & 3];
                    };
                }
                Decrypt = buffer => {
                    read (buffer);
                    transform ();
                    decrypt_name (buffer);
                };
            }
        }
    }
}
