//! \file       ArcXuse.cs
//! \date       Sun Aug 09 07:47:18 2015
//! \brief      Xuse/Eternal resource archives.
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

namespace GameRes.Formats.Xuse
{
    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Xuse"; } }
        public override string Description { get { return "Xuse/Eternal resource archive"; } }
        public override uint     Signature { get { return 0x4F4B494D; } } // 'MIKO'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public ArcOpener ()
        {
            Signatures = new uint[] { 0x4F4B494D, 0x43524158 };
            Extensions = new string[] { "arc", "xarc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (0x1001 != file.View.ReadInt16 (0xA))
                return null;
            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;
            int mode = file.View.ReadInt32 (0xC);
            long cadr_offset;
            if (0 == (mode & 0xF))
            {
                if (!file.View.AsciiEqual (0x16, "DFNM"))
                    return null;
                cadr_offset = file.View.ReadInt64 (0x1A);
            }
            else
                throw new NotSupportedException ("Not supported Xuse archive version");

            int ndix_offset = 0x24;
            if (!file.View.AsciiEqual (ndix_offset, "NDIX"))
                return null;
            if (cadr_offset > file.View.Reserve (0, (uint)cadr_offset))
                return null;
            int index_length = 8 * count;
            int filenames_offset = ndix_offset + 8 + 2 * index_length;
            if (!file.View.AsciiEqual (filenames_offset, "CTIF"))
                return null;

            var dir = new List<Entry> (count);
            using (var cadr_view = file.CreateFrame())
            {
                uint cadr_size = 4 + 12 * (uint)count;
                if (cadr_size > cadr_view.Reserve (cadr_offset, cadr_size)
                    || !cadr_view.AsciiEqual (cadr_offset, "CADR"))
                    return null;
                ndix_offset += 6;
                cadr_offset += 6;
                var name_buf = new byte[0x40];
                for (int i = 0; i < count; ++i)
                {
                    uint entry_offset = file.View.ReadUInt32 (ndix_offset);
                    if (0x1001 != file.View.ReadUInt16 (entry_offset))
                        return null;
                    var name_length = file.View.ReadUInt16 (entry_offset+6);
                    if (name_length > name_buf.Length)
                        name_buf = new byte[name_length];
                    file.View.Read (entry_offset+0xA, name_buf, 0, name_length);
                    for (int n = 0; n < name_length; ++n)
                        name_buf[n] ^= 0x56;

                    var name = Encodings.cp932.GetString (name_buf, 0, name_length);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = cadr_view.ReadInt64 (cadr_offset);
                    if (entry.Offset >= file.MaxOffset)
                        return null;
                    dir.Add (entry);

                    ndix_offset += 8;
                    cadr_offset += 12;
                }
            }
            foreach (var entry in dir)
            {
                if (!file.View.AsciiEqual (entry.Offset, "DATA"))
                    return null;
                entry.Size = file.View.ReadUInt32 (entry.Offset+0x18);
                entry.Offset += 0x1E;
            }
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class KotoriOpener : ArchiveFormat
    {
        public override string         Tag { get { return "KOTORI/Xuse"; } }
        public override string Description { get { return "Xuse/Eternal resource archive"; } }
        public override uint     Signature { get { return 0x4F544F4B; } } // 'KOTO'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public KotoriOpener ()
        {
            Extensions = new string[] { "bin" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "KOTORI") || 0x1A1A00 != file.View.ReadInt32 (6))
                return null;
            int count = file.View.ReadUInt16 (0x14);
            if (0x0100A618 != file.View.ReadInt32 (0x10) || !IsSaneCount (count))
                return null;
            string base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint current_offset = 0x18;
            long next_offset = file.View.ReadUInt32 (current_offset);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry {
                    Name = string.Format ("{0}#{1:D4}.ogg", base_name, i),
                    Type = "audio",
                    Offset = next_offset,
                };
                if (i+1 != count)
                {
                    current_offset += 6;
                    next_offset = file.View.ReadUInt32 (current_offset);
                }
                else
                    next_offset = file.MaxOffset;
                entry.Size = (uint)(next_offset - entry.Offset);
                if (entry.Size >= 0x32)
                {
                    entry.IsPacked = true;
                    entry.UnpackedSize = entry.Size - 0x32;
                }
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (entry.Size < 0x32 || !arc.File.View.AsciiEqual (entry.Offset, "KOTORi")
                || 0x001A1A00 != arc.File.View.ReadInt32 (entry.Offset+6)
                || 0x0100A618 != arc.File.View.ReadInt32 (entry.Offset+0x10))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var key = new byte[0x10];
            arc.File.View.Read (entry.Offset+0x20, key, 0, 0x10);
            uint length = entry.Size - 0x32;
            var data = new byte[length];
            length = (uint)arc.File.View.Read (entry.Offset+0x32, data, 0, length);
            for (uint i = 0; i < length; ++i)
            {
                data[i] ^= key[i&0xF];
            }
            return new MemoryStream (data);
        }
    }
}
