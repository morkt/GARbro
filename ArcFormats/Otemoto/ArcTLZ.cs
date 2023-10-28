//! \file       ArcTLZ.cs
//! \date       2017 Dec 28
//! \brief      Otemoto resource archive.
//
// Copyright (C) 2017 by morkt
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

namespace GameRes.Formats.Otemoto
{
    [Export(typeof(ArchiveFormat))]
    public class TlzOpener : ArchiveFormat
    {
        public override string         Tag { get { return "TLZ"; } }
        public override string Description { get { return "Otemoto resource archive"; } }
        public override uint     Signature { get { return 0x315A4C54; } } // 'TLZ1'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public TlzOpener ()
        {
            ContainedFormats = new[] { "BMP", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0xC);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (4);
            if (index_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry {
                    UnpackedSize = file.View.ReadUInt32 (index_offset),
                    Size         = file.View.ReadUInt32 (index_offset+4),
                    Offset       = file.View.ReadUInt32 (index_offset+8),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                uint name_length = file.View.ReadUInt32 (index_offset+0xC);
                if (0 == name_length || name_length > 0x100)
                    return null;
                entry.Name = file.View.ReadString (index_offset+0x10, name_length);
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name, ContainedFormats);
                entry.IsPacked = entry.UnpackedSize != entry.Size;
                dir.Add (entry);
                index_offset += 0x10 + name_length;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "SNR")]
    [ExportMetadata("Target", "SCR")]
    internal class SnrFormat : ResourceAlias { }
}
