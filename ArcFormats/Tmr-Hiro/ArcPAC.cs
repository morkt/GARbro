//! \file       ArcPAC.cs
//! \date       Wed Dec 23 15:37:30 2015
//! \brief      Tmr-Hiro ADV System archives.
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.TmrHiro
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/TMR-HIRO"; } }
        public override string Description { get { return "Tmr-Hiro ADV System resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PacOpener ()
        {
            Extensions = new string[] { "pac" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;
            uint name_length = file.View.ReadByte (2);
            if (0 == name_length)
                return null;
            uint data_offset = file.View.ReadUInt32 (3);
            if (data_offset >= file.MaxOffset)
                return null;
            int version;
            uint index_size = 7 + (name_length + 8) * (uint)count;
            if (data_offset == index_size)
                version = 1;
            else if (data_offset == index_size + 4 * (uint)count)
                version = 2;
            else
                return null;

            var dir = new List<Entry> (count);
            uint index_offset = 7;
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, name_length);
                index_offset += name_length;
                var entry = new Entry { Name = name };
                if (1 == version)
                {
                    entry.Offset = file.View.ReadUInt32 (index_offset) + data_offset;
                    entry.Size   = file.View.ReadUInt32 (index_offset+4);
                    index_offset += 8;
                }
                else
                {
                    entry.Offset = file.View.ReadInt64 (index_offset) + data_offset;
                    entry.Size   = file.View.ReadUInt32 (index_offset+8);
                    index_offset += 12;
                }
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLower();
            foreach (var entry in dir)
            {
                var signature = file.View.ReadUInt32 (entry.Offset);
                if (0x5367674F == signature) // 'OggS'
                {
                    entry.Name = Path.ChangeExtension (entry.Name, "ogg");
                    entry.Type = "audio";
                }
                else if ((((signature & 0xFF) == 1 || (signature & 0xFF) == 2)) && arc_name.Contains ("grd"))
                {
                    entry.Name = Path.ChangeExtension (entry.Name, "grd");
                    entry.Type = "image";
                }
                else if (0x44 == (signature & 0xFF) && entry.Size-9 == file.View.ReadUInt32 (entry.Offset+5))
                {
                    entry.Type = "audio";
                }
                else if (6 == file.View.ReadInt16 (entry.Offset+4) && 0x140050 == file.View.ReadUInt32 (entry.Offset+6))
                {
                    entry.Type = "script";
                    if ("srp" == arc_name)
                        entry.Name = Path.ChangeExtension (entry.Name, "srp");
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if ("script" != entry.Type
                || 6 != arc.File.View.ReadInt16 (entry.Offset+4)
                || 0x140050 != arc.File.View.ReadUInt32 (entry.Offset+6))
                return base.OpenEntry (arc, entry);
            int record_count = arc.File.View.ReadInt32 (entry.Offset);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int pos = 4;
            for (int i = 0; i < record_count && pos + 2 <= data.Length; ++i)
            {
                int chunk_size = LittleEndian.ToUInt16 (data, pos) - 4;
                pos += 6;
                if (pos + chunk_size > data.Length)
                    return base.OpenEntry (arc, entry);
                for (int j = 0; j < chunk_size; ++j)
                {
                    data[pos] = Binary.RotByteR (data[pos], 4);
                    ++pos;
                }
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
