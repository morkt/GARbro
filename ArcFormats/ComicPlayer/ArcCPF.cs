//! \file       ArcCPF.cs
//! \date       2026-08-18
//! \brief      Interactive Comic Player resource archive.
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
using GameRes.Compression;

namespace GameRes.Formats.ComicPlayer
{
    internal class CpEntry : PackedEntry
    {
        public bool IsEncrypted;
    }

    [Export(typeof(ArchiveFormat))]
    public class CpfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CPF"; } }
        public override string Description { get { return "Interactive Comic Player resource archive"; } }
        public override uint     Signature { get { return 0x394D4349; } } // 'ICM95'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CpfOpener ()
        {
            Signatures = new[] { 0x394D4349u, 0x4E494D43u, 0x4C334D43u, 0u };
            Extensions = new[] { "cpf", "ci", "cml", "exe" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = 0;
            if (0x5A4D == file.View.ReadUInt16 (0))
            {
                var exe = new ExeFile (file);
                base_offset = (uint)exe.Overlay.Offset + 0x100;
            }
            var valid_magics = new List<string> { "ICM95", "CMINST", "CM3PKG", "CM3LIB" };
            var magic = file.View.ReadString (base_offset, 8);
            if (!valid_magics.Contains (magic))
                return null;

            int count = file.View.ReadInt32 (base_offset + 0x108);
            if (!IsSaneCount (count))
                return null;
 
            uint index_offset = base_offset + 0x10C;
            uint data_offset = index_offset + (uint)count * 0x110;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; i++)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<CpEntry> (name);
                index_offset += 0x100;
                uint flags = file.View.ReadUInt32 (index_offset);
                entry.IsPacked = (flags & 0x80000000) != 0;
                entry.Offset = file.View.ReadUInt32 (index_offset + 8) + data_offset;
                entry.Size = file.View.ReadUInt32 (index_offset + 12);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasAnyOfExtensions (".csf", ".cf3"))
                    entry.Type = "script";
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var cent = entry as CpEntry;
            var input = arc.File.CreateStream (cent.Offset, cent.Size);
            if (cent.IsPacked)
                return new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
