//! \file       ArcPACK.cs
//! \date       2018 Mar 20
//! \brief      AIMS engine resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Cryptography;

namespace GameRes.Formats.Aims
{
    internal class LunaArchive : ArcFile
    {
        public readonly byte[] Key;

        public LunaArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte[] key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/AIMS"; } }
        public override string Description { get { return "AIMS engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "p", "mus", "pac" };
        }

        // entry record
        // 0x00 name
        // 0x40 name crc32
        // 0x44 data crc32
        // 0x48 offset
        // 0x4C size

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x40);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x48);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x4C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x50;
            }
            return new LunaArchive (file, this, dir, DefaultKey);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var larc = arc as LunaArchive;
            if (null == arc || !arc.File.View.AsciiEqual (entry.Offset, "LZSS"))
                return base.OpenEntry (arc, entry);

            uint unpacked_size = arc.File.View.ReadUInt32 (entry.Offset+4);
            Stream input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            if (larc.Key != null)
            {
                var bf = new Blowfish (larc.Key);
                input = new InputCryptoStream (input, bf.CreateDecryptor());
                return new LimitStream (input, unpacked_size);
            }
            else
            {
                var lz = new LzssStream (input);
                lz.Config.FrameInitPos = 0xFF0;
                return lz;
            }
        }

        static readonly byte[] DefaultKey = {
            0x7D, 0x73, 0xF6, 0xE4, 0xF5, 0x81, 0x5F, 0x7C, 0x78, 0x30, 0xC2, 0x36, 0xEA, 0x3E, 0x8A, 0x76,
            0xF7, 0xE0, 0x48, 0xB5, 0x85, 0xD7, 0x77, 0x49, 0x4C, 0x3D, 0xF5, 0x0C, 0xBB, 0xFB, 0x2E, 0x44,
            0xFE, 0x25, 0xB7, 0xEB, 0xC7, 0xD9, 0x33, 0xAB, 0xA8, 0x2C, 0x64, 0xE8, 0xF0, 0xBD, 0xEB, 0x8D,
            0x9D, 0x1D, 0xA2, 0xFC, 0x59, 0x09, 0xAA, 0xA4,
        };
    }
}
