//! \file       ArcPAC.cs
//! \date       2018 Oct 21
//! \brief      Penguin Works resource archive.
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
using GameRes.Formats.UMeSoft;

// [020809][Penguin Works] Ryouki no Ori Dai 4 Shou

namespace GameRes.Formats.PenguinWorks
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/PENGUIN"; } }
        public override string Description { get { return "Penguin Works resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<string, string> ContentFormats = new Dictionary<string, string> {
            { "TAK", "BIN" },
            { "VIS", "BMP" },
            { "EFT", "WAV" },
            { "BGM", "STR" },
        };

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".pac"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name).ToUpperInvariant();
            var name_format = base_name + "{0:D4}";
            if (ContentFormats.ContainsKey (base_name))
                name_format += '.' + ContentFormats[base_name];

            uint index_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint id = file.View.ReadUInt32 (index_offset);
                var name = string.Format (name_format, id);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                entry.Size   = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 12;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = (PackedEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (!pent.IsPacked)
            {
                var header = input.ReadHeader (13);
                if (!header.AsciiEqual (2, "ike"))
                {
                    input.Position = 0;
                    return input;
                }
                pent.IsPacked = true;
                pent.UnpackedSize = (uint)IkeReader.DecodeSize (header[10], header[11], header[12]);
            }
            using (input)
            {
                input.Position = 13;
                var reader = new IkeReader (input, (int)pent.UnpackedSize);
                var data = reader.Unpack();
                return new BinMemoryStream (data, entry.Name);
            }
        }
    }
}
