//! \file       ArcMF.cs
//! \date       2023 Aug 09
//! \brief      Multimedia Fusion archive.
//
// Copyright (C) 2023 by morkt
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

using GameRes.Compression;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;

namespace GameRes.Formats.ClickTeam
{
    [Export(typeof(ArchiveFormat))]
    public class MfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MFS"; } }
        public override string Description { get { return "Multimedia Fusion resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public MfOpener ()
        {
            Signatures = new uint[] { 0x77777777, 0 }; // 'wwww'
            Extensions = new string[] { "" };
        }

        static readonly byte[] s_mfs_header = {
            (byte)'w', (byte)'w', (byte)'w', (byte)'w', 0x49, 0x87, 0x47, 0x12,
        };

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (0x5A4D == file.View.ReadUInt16 (0)) // 'MZ'
            {
                var exe = new ExeFile (file);
                base_offset = exe.Overlay.Offset;
            }
            if (base_offset + 0x20 > file.MaxOffset || !file.View.BytesEqual (base_offset, s_mfs_header))
                return null;

            int count = file.View.ReadInt32 (base_offset + 0x1C);
            if (!IsSaneCount (count))
                return null;

            long index_pos = base_offset + 0x20;
            var dir = new List<Entry> (count);
            var name_buffer = new byte[520];
            for (int i = 0; i < count; ++i)
            {
                int name_length = file.View.ReadUInt16 (index_pos) * 2;
                if (name_length > name_buffer.Length)
                    return null;
                file.View.Read (index_pos + 2, name_buffer, 0, (uint)name_length);
                var name = Encoding.Unicode.GetString (name_buffer, 0, name_length);
                index_pos += 2 + name_length;
                var entry = Create<PackedEntry> (name);
                entry.IsPacked = name != "mmfs2.dll";
                entry.Size = file.View.ReadUInt32 (index_pos + 4);
                entry.Offset = index_pos + 8;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos = entry.Offset + entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (pent.IsPacked)
                input = new ZLibStream (input, CompressionMode.Decompress);
            return input;
        }
    }
}
