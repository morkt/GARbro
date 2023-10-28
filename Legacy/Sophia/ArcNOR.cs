//! \file       ArcNOR.cs
//! \date       2023 Sep 26
//! \brief      Sophia resource archive.
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

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [991210][Sophia] Film Noir

namespace GameRes.Formats.Sophia
{
    internal class NorEntry : PackedEntry
    {
        public int  Method;
    }

    [Export(typeof(ArchiveFormat))]
    public class NorOpener : ArchiveFormat
    {
        public override string         Tag => "NOR";
        public override string Description => "Sophia resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!file.View.AsciiEqual (4, "NRCOMB01\0") || !IsSaneCount (count))
                return null;
            var dir = new List<Entry> (count);
            using (var index = file.CreateStream())
            {
                index.Position = 0x10;
                for (int i = 0; i < count; ++i)
                {
                    uint offset = index.ReadUInt32();
                    uint size   = index.ReadUInt32();
                    string name = index.ReadCString();
                    var entry = Create<NorEntry> (name);
                    entry.Offset = offset;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var nent = (NorEntry)entry;
            if (!nent.IsPacked)
            {
                if (nent.Method == 0x1F4 || nent.Method == 0x67
                    || !arc.File.View.AsciiEqual (nent.Offset, "NCMB01"))
                    return base.OpenEntry (arc, nent);
                nent.Method = arc.File.View.ReadInt32 (nent.Offset+0x28);
                if (nent.Method == 0x1F4 || nent.Method == 0x67)
                {
                    nent.Size = arc.File.View.ReadUInt32 (nent.Offset+0x24);
                    nent.Offset += 0x2C;
                    return base.OpenEntry (arc, nent);
                }
                nent.IsPacked = true;
                nent.UnpackedSize = arc.File.View.ReadUInt32 (nent.Offset+0x24);
                nent.Size = arc.File.View.ReadUInt32 (nent.Offset+0x10);
                nent.Offset += 0x2C;
            }
            using (var input = arc.File.CreateStream (nent.Offset, nent.Size))
            {
                var output = new byte[nent.UnpackedSize];
                NcmbDecompress (input, output);
                return new BinMemoryStream (output, nent.Name);
            }
        }

        internal static void NcmbDecompress (IBinaryStream input, byte[] output)
        {
            var dict = new int[0xC00];
            int root = input.ReadInt32();
            int tree_size = input.ReadInt32();
            int unpacked_size = input.ReadInt32();
            int count = root + tree_size - 0xFF;
            while (count --> 0)
            {
                int token = 6 * input.ReadInt32();
                dict[token    ] = input.ReadInt32();
                dict[token + 1] = input.ReadInt32();
            }
            if (unpacked_size > 0)
            {
                int cur_byte = 0;
                int mask = 0;
                for (int dst = 0; dst < unpacked_size; ++dst)
                {
                    int token = root;
                    do
                    {
                        if (0 == mask)
                        {
                            cur_byte = input.ReadUInt8();
                            mask = 0x80;
                        }
                        if ((cur_byte & mask) != 0)
                            token = dict[6 * token + 1];
                        else
                            token = dict[6 * token];
                        mask >>= 1;
                    }
                    while (dict[6 * token] != -1);
                    output[dst] = (byte)token;
                }
            }
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "M")]
    [ExportMetadata("Target", "MP3")]
    [ExportMetadata("Type", "audio")]
    public class MFormat : ResourceAlias { }
}
