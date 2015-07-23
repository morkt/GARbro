//! \file       ArcTactics.cs
//! \date       Thu Jul 23 16:27:55 2015
//! \brief      Tactics archive file implementation.
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
using System.Linq;
using System.Security.Cryptography;
using GameRes.Utility;

namespace GameRes.Formats.Tactics
{
    internal class TacticsArcFile : ArcFile
    {
        public byte[] Password;

        public TacticsArcFile (ArcView file, ArchiveFormat format, ICollection<Entry> dir, byte[] pass)
            : base (file, format, dir)
        {
            Password = pass;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class ArcOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Tactics"; } }
        public override string Description { get { return "Tactics archive file"; } }
        public override uint     Signature { get { return 0x54434154; } } // 'TACT'
        public override bool  IsHierarchic { get { return true; } }
        public override bool     CanCreate { get { return false; } }

        public ArcOpener ()
        {
            Extensions = new string[] { "arc" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "ICS_ARC_FILE"))
                return null;
            uint packed_size = file.View.ReadUInt32 (0x10);
            uint unpacked_size = file.View.ReadUInt32 (0x14);
            int count = file.View.ReadInt32 (0x18);
            if (!IsSaneCount (count))
                return null;

            var index = new byte[unpacked_size];
            using (var input = file.CreateStream (0x20, packed_size))
            using (var xored = new CryptoStream (input, new NotTransform(), CryptoStreamMode.Read))
            using (var lzss = new LzssStream (xored))
                lzss.Read (index, 0, index.Length);

            int index_offset = Array.IndexOf (index, (byte)0);
            if (-1 == index_offset || 0 == index_offset)
                return null;
            var password = index.Take (index_offset++).ToArray();
            long base_offset = 0x20 + packed_size;
            
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new PackedEntry();
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset) + base_offset;
                entry.Size   = LittleEndian.ToUInt32 (index, index_offset + 4);
                entry.UnpackedSize = LittleEndian.ToUInt32 (index, index_offset + 8);
                entry.IsPacked = entry.UnpackedSize != 0;
                int name_len = LittleEndian.ToInt32 (index, index_offset + 0xC);
                entry.Name = Encodings.cp932.GetString (index, index_offset+0x18, name_len);
                entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x18 + name_len;
            }
            return new TacticsArcFile (file, this, dir, password);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var tarc = arc as TacticsArcFile;
            if (null == tarc)
                return arc.File.CreateStream (entry.Offset, entry.Size);

            var data = new byte[entry.Size];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            int p = 0;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] ^= tarc.Password[p++];
                if (p == tarc.Password.Length)
                    p = 0;
            }
            var input = new MemoryStream (data);
            var tent = entry as PackedEntry;
            if (null == tent || !tent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }
}
