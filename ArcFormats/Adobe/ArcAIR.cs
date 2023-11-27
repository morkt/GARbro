//! \file       ArcAIR.cs
//! \date       2023 Aug 22
//! \brief      Adobe AIR resource archive.
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

using GameRes.Utility;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GameRes.Formats.Adobe
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get => "DAT/AIR"; }
        public override string Description { get => "Adobe AIR resource archive"; }
        public override uint     Signature { get => 0; }
        public override bool  IsHierarchic { get => false; }
        public override bool      CanWrite { get => false; }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_pos = Binary.BigEndian (file.View.ReadUInt32 (0));
            if (index_pos >= file.MaxOffset || 0 == index_pos || file.MaxOffset > 0x40000000)
                return null;
            uint index_size = (uint)(file.MaxOffset - index_pos);
            if (index_size > 0x100000) // arbitrary max size for compressed index
                return null;
            using (var input = file.CreateStream (index_pos, index_size))
            using (var unpacked = new DeflateStream (input, CompressionMode.Decompress))
            using (var index = new BinaryStream (unpacked, file.Name))
            {
                if (0x0A != index.ReadUInt8() ||
                    0x0B != index.ReadUInt8() ||
                    0x01 != index.ReadUInt8())
                    return null;
                var name_buffer = new byte[0x80];
                var dir = new List<Entry>();
                while (index.PeekByte() != -1)
                {
                    int length = index.ReadUInt8();
                    if (0 == (length & 1))
                        return null;
                    length >>= 1;
                    if (0 == length)
                        break;
                    index.Read (name_buffer, 0, length);
                    var name = Encoding.UTF8.GetString (name_buffer, 0, length);
                    if (0x09 != index.ReadUInt8() ||
                        0x05 != index.ReadUInt8() ||
                        0x01 != index.ReadUInt8())
                        return null;
                    if (0x04 != index.ReadUInt8()) // invalid number signature
                        return null;
                    uint offset = ReadInteger (index);
                    if (0x04 != index.ReadUInt8())
                        return null;
                    uint size = ReadInteger (index);
                    var entry = Create<PackedEntry> (name);
                    entry.Offset = offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (0 == entry.Size)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new DeflateStream (input, CompressionMode.Decompress);
        }

        internal static uint ReadInteger (IBinaryStream input)
        {
            uint u = input.ReadUInt8();
            if (u < 0x80)
                return u;
            u = (u & 0x7F) << 7;
            uint b = input.ReadUInt8();
            if (b < 0x80)
                return u | b;
            u = (u | b & 0x7F) << 7;
            b = input.ReadUInt8();
            if (b < 0x80)
                return u | b;
            u = (u | b & 0x7F) << 8;
            return u | input.ReadUInt8();
        }
    }
}
