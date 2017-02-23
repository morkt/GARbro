//! \file       ArcDAF.cs
//! \date       Thu Feb 23 01:26:17 2017
//! \brief      Cadath resource archive format.
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
using GameRes.Utility;

namespace GameRes.Formats.Cadath
{
    [Export(typeof(ArchiveFormat))]
    public class DafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/DAF"; } }
        public override string Description { get { return "Cadath resource archive"; } }
        public override uint     Signature { get { return 0x1A464144; } } // 'DAF'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                var name = file.View.ReadString (index_offset+8, 0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith (".snr", System.StringComparison.InvariantCultureIgnoreCase)
                || !arc.File.View.AsciiEqual (entry.Offset, "SNR\x1A"))
                return base.OpenEntry (arc, entry);
            try
            {
                var data = arc.File.View.ReadBytes (entry.Offset+12, entry.Size-12);
                DecryptSnr (data);
                CgfDecoder.Decrypt (data, data.Length);
//                uint checksum = LittleEndian.ToUInt32 (data, 0);
//                uint crc = Crc32Normal.Compute (data, 4, data.Length-4);
                var input = new MemoryStream (data, 4, data.Length-4);
                return new ZLibStream (input, CompressionMode.Decompress);
            }
            catch
            {
                return base.OpenEntry (arc, entry);
            }
        }

        void DecryptSnr (byte[] data)
        {
            byte key = 0x84;
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] -= key;
                for (int count = ((i & 0xF) + 2) / 3; count > 0; --count)
                {
                    key += 0x99;
                }
            }

        }
    }
}
