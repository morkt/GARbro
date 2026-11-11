//! \file       ArcCP3.cs
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
using GameRes.Utility;

namespace GameRes.Formats.ComicPlayer
{
    [Export(typeof(ArchiveFormat))]
    public class Cp3Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CP3/CM"; } }
        public override string Description { get { return "Interactive Comic Player resource archive"; } }
        public override uint     Signature { get { return 0x50334D43; } } // 'CM3PKG'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Cp3Opener ()
        {
            Signatures = new[] { 0x50334D43u, 0u };
            Extensions = new[] { "cp3", "exe" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint base_offset = 0;
            if (0x5A4D == file.View.ReadUInt16 (0))
            {
                var exe = new ExeFile (file);
                base_offset = (uint)exe.Overlay.Offset + 0x100;
            }
            var magic = file.View.ReadString (base_offset, 8);
            if (magic != "CM3PKG")
                return null;

            long offset = base_offset + 0x218;
            var dir = new List<Entry> ();
            while (offset < file.MaxOffset)
            {
                ushort tag = file.View.ReadUInt16 (offset);
                if (tag == 0x5045) // 'EP'
                    break;
                else if (tag != 0x494C) // 'LI'
                    return null;
                offset += 6;
                uint name_length = file.View.ReadByte (offset);
                var name = file.View.ReadString (offset + 1, name_length);
                if (string.IsNullOrEmpty (name))
                    return null;
                var entry = Create<CpEntry> (name);
                offset += name_length + 9;
                uint flags = Binary.BigEndian (file.View.ReadUInt32 (offset - 8));
                entry.IsPacked = (flags & 0x80000000) != 0;
                entry.IsEncrypted = (flags & 0x80000) != 0;
                entry.Offset = offset + 12;
                entry.Size = Binary.BigEndian (file.View.ReadUInt32 (offset));
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (name.HasExtension (".cf3"))
                    entry.Type = "script";
                dir.Add (entry);
                offset = entry.Offset + entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var cent = entry as CpEntry;
            Stream input = arc.File.CreateStream (cent.Offset, cent.Size);
            if (cent.IsEncrypted)
            {
                var key = arc.File.View.ReadBytes (0x118, 0x100);
                input = new ByteStringEncryptedStream (input, key);
            }
            if (cent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
