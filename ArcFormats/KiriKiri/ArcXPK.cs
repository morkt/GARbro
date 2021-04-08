//! \file       ArcXPK.cs
//! \date       2017 Dec 05
//! \brief      KAG System resource archive.
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
using System.Text;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.KiriKiri
{
    [Export(typeof(ArchiveFormat))]
    public class XpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "XPK"; } }
        public override string Description { get { return "KAG System resource archive"; } }
        public override uint     Signature { get { return 0x314B5058; } } // 'XPK1'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public XpkOpener ()
        {
            Signatures = new uint[] { 0x314B5058, 0 };
        }

        static readonly byte[] SignatureBytes = Encoding.ASCII.GetBytes ("XPK1\x1A");

        public override ArcFile TryOpen (ArcView file)
        {
            long base_offset = 0;
            if (0x5A4D == file.View.ReadUInt16 (0)) // 'MZ'
                base_offset = Xp3Opener.SkipExeHeader (file, SignatureBytes);
            if (!file.View.BytesEqual (base_offset, SignatureBytes))
                return null;
            using (var index = file.CreateStream())
            {
                index.Position = base_offset+10;
                int count = (int)ReadUInt (index);
                if (!IsSaneCount (count))
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    long offset = ReadUInt (index) + base_offset;
                    uint size = ReadUInt (index);
                    uint unpacked_size = ReadUInt (index);
                    index.ReadInt16();
                    var name = index.ReadCString();
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    entry.UnpackedSize = unpacked_size;
                    if (!entry.CheckPlacement (file.MaxOffset) && !(offset == file.MaxOffset && size == 0))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        internal static uint ReadUInt (IBinaryStream input)
        {
            if (input.ReadByte() != 4)
                throw new InvalidFormatException();
            return Binary.BigEndian (input.ReadUInt32());
        }

        public override Stream OpenEntry(ArcFile arc, Entry entry)
        {
            PackedEntry packed_entry = entry as PackedEntry;
            if (null == packed_entry || packed_entry.Size == packed_entry.UnpackedSize)
                return arc.File.CreateStream(entry.Offset, entry.Size);
            return new ZLibStream(arc.File.CreateStream(entry.Offset, entry.Size), CompressionMode.Decompress);
        }
    }
}
