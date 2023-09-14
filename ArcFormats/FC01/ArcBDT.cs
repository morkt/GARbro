//! \file       ArcBDT.cs
//! \date       2023 Sep 10
//! \brief      RPA resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;

// [021228][Fairytale Kagetsu Gumi] Tsuki Jong

namespace GameRes.Formats.FC01
{
    [Export(typeof(ArchiveFormat))]
    public partial class BdtOpener : PakOpener
    {
        public override string         Tag => "BDT";
        public override string Description => "Fairytale resource archive";
        public override uint     Signature => 0x4B434150; // 'PACK'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public BdtOpener ()
        {
            Signatures = new[] { 0x4B434150u, 0u };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint signature = file.View.ReadUInt32 (0);
            if (signature != this.Signature)
                return BogusMediaArchive (file, signature);
            var arc_name = Path.GetFileNameWithoutExtension (file.Name).ToLowerInvariant();
            if (!arc_name.StartsWith ("dt0"))
                return null;

            uint index_pos = 12;
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count) || file.MaxOffset <= index_pos)
                return null;
            int entry_size = file.View.ReadInt32 (8);

            string arc_num_str = arc_name.Substring (3);
            int arc_num = int.Parse (arc_num_str, NumberStyles.HexNumber);
            var dir = ReadVomIndex (file, arc_num, count, entry_size);
            if (null == dir)
                return null;
            var arc_key = GetKey (arc_num);
            return new AgsiArchive (file, this, dir, arc_key);
        }

        List<Entry> ReadVomIndex (ArcView file, int arc_num, int count, int record_size)
        {
            if (4 == arc_num) // dt004.bdt is an archive of indexes
            {
                var reader = new IndexReader (file, count, record_size);
                return reader.ReadIndex();
            }
            var dt4name = VFS.ChangeFileName (file.Name, "dt004.bdt");
            using (var dt4file = VFS.OpenView (dt4name))
            {
                var dt4arc = TryOpen (dt4file);
                if (null == dt4arc)
                    return null;
                using (dt4arc)
                {
                    int vom_idx = arc_num;
                    if (arc_num > 4)
                        --vom_idx;
                    var voms = string.Format ("vom{0:D3}.dat", vom_idx);
                    var vom_entry = dt4arc.Dir.First (e => e.Name == voms);
                    using (var input = dt4arc.OpenEntry (vom_entry))
                    using (var index = BinaryStream.FromStream (input, vom_entry.Name))
                    {
                        var reader = new IndexReader (file, count, record_size);
                        return reader.ReadIndex (index);
                    }
                }
            }
        }

        ArcFile BogusMediaArchive (ArcView file, uint signature)
        {
            if (!file.Name.HasExtension (".bdt"))
                return null;
            string ext = null;
            string type = "";
            if (OggAudio.Instance.Signature == signature)
            {
                ext = ".ogg";
                type = "audio";
            }
            else if (AudioFormat.Wav.Signature == signature && file.View.AsciiEqual (8, "AVI "))
            {
                ext = ".avi";
            }
            if (null == ext)
                return null;
            var name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> {
                new Entry {
                    Name = name + ext,
                    Type = type,
                    Offset = 0,
                    Size = (uint)file.MaxOffset
                }
            };
            return new ArcFile (file, this, dir);
        }

        byte[] GetKey (int index)
        {
            int src = index * 8;
            if (src + 8 > KeyOffsetTable.Length)
                throw new ArgumentException ("Invalid AGSI key index.");
            var key = new byte[8];
            key[0] = KeySource[KeyOffsetTable[src++]];
            key[1] = KeySource[KeyOffsetTable[src++]];
            key[2] = KeySource[KeyOffsetTable[src++]];
            key[3] = KeySource[KeyOffsetTable[src++] + 480];
            key[4] = KeySource[KeyOffsetTable[src++] + 480];
            key[5] = KeySource[KeyOffsetTable[src++] + 480];
            key[6] = KeySource[KeyOffsetTable[src++] + 480];
            key[7] = KeySource[KeyOffsetTable[src++] + 480];
            return key;
        }
    }
}
