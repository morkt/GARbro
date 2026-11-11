//! \file       ArcDAT.cs
//! \date       2026-02-04
//! \brief      Nekopack archive format implementation.
//
// Copyright (C) 2016 by morkt
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
using GameRes.Utility;

namespace GameRes.Formats.Neko
{
    internal class NekoItaEntry : Entry
    {
        public byte ExtraKey;
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/NEKOPACK"; } }
        public override string Description { get { return "NekoPack resource archive"; } }
        public override uint     Signature { get { return 0x4f4b454e; } } // "NEKO"
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "PACK"))
                return null;

            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;

            var table = new uint[0x270];
            int pos = 0;
            InitTable (table, 0x9999);

            var dir = new List<Entry> (count);
            uint index_offset = 0x14;
            for (int i = 0; i < count; ++i)
            {
                byte extra = file.View.ReadByte (index_offset);
                uint name_length = file.View.ReadByte (index_offset + 1);
                var name_buffer = file.View.ReadBytes (index_offset + 2, name_length);
                pos = Decrypt (name_buffer, 0, (int)name_length, table, pos);
                var name = Binary.GetCString (name_buffer, 0, (int)name_length);
                var entry = Create<NekoItaEntry> (name);
                index_offset += name_length + 2;
                entry.Offset = file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset + 4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                if (entry.Name.HasExtension (".img"))
                    entry.Type = "";
                entry.ExtraKey = extra;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nent = (NekoItaEntry)entry;
            var input = arc.File.View.ReadBytes (nent.Offset, nent.Size);
            if (nent.ExtraKey != 0)
            {
                var table = new uint[0x270];
                InitTable (table, (uint)(0x9999 + nent.ExtraKey));
                Decrypt (input, 0, (int)nent.Size, table, 0);
            }
            return new BinMemoryStream (input);
        }

        void InitTable (uint[] table, uint key)
        {
            for (int i = 0; i < table.Length; i++)
            {
                key *= 0x10dcd;
                table[i] = key;
            }
        }

        int Decrypt (byte[] data, int pos, int length, uint[] table, int table_pos)
        {
            for (int i = pos; i < pos + length; i++)
            {
                if (table_pos == table.Length)
                {
                    InitTable (table, table[table.Length - 1]);
                    table_pos = 0;
                }
                uint key = table[table_pos++];
                key ^= key >> 11;
                key ^= (key << 7) & 0x31518a63;
                key ^= (key << 15) & 0x17f1ca43;
                key ^= key >> 18;
                data[i] ^= (byte)key;
            }
            return table_pos;
        }
    }
}
