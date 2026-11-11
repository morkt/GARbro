//! \file       ArcTLP.cs
//! \date       2025-12-22
//! \brief      FrontWing resource archive.
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

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;
using GameRes.Compression;

namespace GameRes.Formats.FrontWing
{
    [Export(typeof(ArchiveFormat))]
    public class TlpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/TLP"; } }
        public override string Description { get { return "'Time Leap Paradise' resource archive"; } }
        public override uint     Signature { get { return 0x5f504c54; } } // 'TLP_DAT'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "DAT"))
                return null;

            int count = file.View.ReadInt32 (0x10);
            if (!IsSaneCount (count))
                return null;

            int index_size = 0x110 * count;
            var index = file.View.ReadBytes (0x20, (uint)index_size);
            byte type = file.View.ReadByte (0x18);
            Decrypt (index, index_size, type);

            var first_name = Binary.GetCString (index, 0, 0x104);
            if (first_name != "data/ajfkur3h45n56d7u78a7nh9u7iI8ny0fau6i4al27we4hfuelnrg")
                return null;

            int index_offset = 0x110;
            var dir = new List<Entry> (count - 1);
            for (int i = 1; i < count; i++)
            {
                var name = Binary.GetCString (index, index_offset, 0x104);
                var entry = Create<PackedEntry> (name);
                entry.Offset = LittleEndian.ToUInt32 (index, index_offset + 0x104);
                entry.UnpackedSize = LittleEndian.ToUInt32 (index, index_offset + 0x108);
                entry.Size = LittleEndian.ToUInt32 (index, index_offset + 0x10C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                index_offset += 0x110;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            byte type = arc.File.View.ReadByte (0x18);
            int len = Math.Min (arc.File.View.ReadByte (0x19) >> 1, data.Length);
            Decrypt (data, len, type);
            return new ZLibStream (new MemoryStream (data), CompressionMode.Decompress);
        }

        void Decrypt (byte[] buffer, int length, byte type = 0)
        {
            if (type == 0)
            {
                byte key = 0xcb;
                for (int i = 0; i < length; i++)
                {
                    buffer[i] = Binary.RotByteR ((byte)(buffer[i] ^ key), 1);
                    key = 1;
                }
            }

            else
            {
                throw new System.NotImplementedException ("decryption type not implemented");
            }
        }
    }
}
