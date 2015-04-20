//! \file       ArcAST.cs
//! \date       Tue Apr 21 02:24:20 2015
//! \brief      AST script engine resource archives.
//
// Copyright (C) 2015 by morkt
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

namespace GameRes.Formats.AST
{
    internal class AstArchive : ArcFile
    {
        public AstArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir)
            : base (arc, impl, dir)
        {
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AST"; } }
        public override string Description { get { return "AST script engine resource archive"; } }
        public override uint     Signature { get { return 0x32435241; } } // 'ARC2'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "" };
            Signatures = new uint[] { 0x32435241, 0x31435241 }; // 'ARC2', 'ARC1'
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadByte (3) - 0x30;
            int count = file.View.ReadInt32 (4);
            if (count <= 0 || count > 0xfffff)
                return null;
            var name_buf = new byte[32];
            var dir = new List<Entry> (count);
            long index_offset = 8;
            uint next_offset = file.View.ReadUInt32 (index_offset);
            for (int i = 0; i < count; ++i)
            {
                uint offset = next_offset;
                uint size   = file.View.ReadUInt32 (index_offset+4);
                int name_length = file.View.ReadByte (index_offset+8);
                if (name_length > name_buf.Length)
                    name_buf = new byte[name_length];
                file.View.Read (index_offset+9, name_buf, 0, (uint)name_length);
                if (2 == version)
                    for (int j = 0; j < name_length; ++j)
                        name_buf[j] ^= 0xff;
                if (i+1 == count)
                    next_offset = (uint)file.MaxOffset;
                else
                    next_offset = file.View.ReadUInt32 (index_offset+9+name_length);
                if (next_offset < offset)
                    return null;
                string name = Encodings.cp932.GetString (name_buf, 0, name_length);
                var entry = new PackedEntry
                {
                    Name = name,
                    Type = FormatCatalog.Instance.GetTypeFromName (name),
                    Offset = offset,
                    Size = next_offset - offset,
                    UnpackedSize = size,
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 9 + name_length;
            }
            if (2 == version)
                return new AstArchive (file, this, dir);
            else
                return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !(arc is AstArchive))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            if (pent.Size == pent.UnpackedSize)
            {
                arc.File.View.Reserve (entry.Offset, entry.Size);
                var sig = arc.File.View.ReadUInt32 (entry.Offset);
                if (0xb8b1af76 == sig)
                {
                    var data = new byte[entry.Size];
                    arc.File.View.Read (entry.Offset, data, 0, entry.Size);
                    for (int i = 0; i < data.Length; ++i)
                        data[i] ^= 0xff;
                    return new MemoryStream (data);
                }
                return arc.File.CreateStream (entry.Offset, entry.Size);
            }
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
            using (var reader = new LzssReader (input, (int)pent.Size, (int)pent.UnpackedSize))
            {
                reader.Unpack();
                var data = reader.Data;
                if (!Binary.AsciiEqual (data, 0, "RIFF") &&
                    !Binary.AsciiEqual (data, 0, "OggS"))
                    for (int i = 0; i < data.Length; ++i)
                        data[i] ^= 0xff;
                return new MemoryStream (data);
            }
        }
    }
}
